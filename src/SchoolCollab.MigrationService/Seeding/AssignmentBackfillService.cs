using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Backfills SubjectId and GradeLevelId on Assignments from legacy coded-value IDs.
/// This is a one-time migration step for PR 4 of the grade-level-setup spec.
///
/// <para><b>Tenancy (global-tenant-filter.md §9.3).</b> GradeLevel/Subject are now
/// strict tenant-scoped entities. The find-or-create runs under the assignment's own
/// tenant via <see cref="ITenantContextAccessor.RunWithExplicitTenantAsync"/> so the
/// "Tenant" query filter scopes the lookup and the save-guard/auto-stamp stamp the
/// assignment's tenant. Assignments with a legacy <c>Guid.Empty</c> tenant (no real
/// tenant) are attributed to the well-known System tenant
/// (<see cref="TenantSeeder.SystemTenantId"/>) — the backfill sink (Q-1).</para>
///
/// <para>Process:</para>
/// <list type="number">
/// <item>Raw-SQL read from Assignments DB: assignments where subject_id IS NULL</item>
/// <item>For each: run under the assignment's tenant, look up coded value in Settings
///   DB by id, find-or-create Subject/GradeLevel in Students DB, raw-SQL update</item>
/// </list>
/// Dev DB has no assignments → backfill is a no-op in dev.
/// </summary>
public sealed class AssignmentBackfillService(
    SettingsDbContext settingsDb,
    StudentsDbContext studentsDb,
    AssignmentsDbContext assignmentsDb,
    ITenantContextAccessor tenantContextAccessor,
    ILogger<AssignmentBackfillService> logger)
{
    public async Task BackfillAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting assignment backfill (SubjectId, GradeLevelId from coded-value IDs)");

        // Open a raw connection to Assignments DB for bulk read/update
        var assignmentsConnection = assignmentsDb.Database.GetDbConnection();
        await assignmentsConnection.OpenAsync(cancellationToken);

        // Get assignments that need backfill (subject_id IS NULL)
        var assignmentsToBackfill = new List<AssignmentBackfillRow>();
        await using (var cmd = assignmentsConnection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT id, subject_coded_value_id, grade_coded_value_id, tenant_id
                FROM assignments
                WHERE subject_id IS NULL";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                var subjectCodedValueId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
                var gradeCodedValueId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
                var tenantId = reader.IsDBNull(3) ? Guid.Empty : reader.GetGuid(3);
                assignmentsToBackfill.Add(new AssignmentBackfillRow(id, subjectCodedValueId, gradeCodedValueId, tenantId));
            }
        }

        if (assignmentsToBackfill.Count == 0)
        {
            logger.LogInformation("No assignments require backfill; exiting");
            await assignmentsConnection.CloseAsync();
            return;
        }

        logger.LogInformation("Found {Count} assignments to backfill", assignmentsToBackfill.Count);

        // Batch lookup coded values from Settings DB (shared-blueprint NULL rows are
        // visible to every tenant via the hybrid filter, so this read works under any
        // tenant context — including the default the MigrationService starts in).
        var codedValueIds = assignmentsToBackfill
                .Where(a => a.SubjectCodedValueId.HasValue)
                .Select(a => a.SubjectCodedValueId!.Value)
                .Concat(assignmentsToBackfill.Where(a => a.GradeCodedValueId.HasValue).Select(a => a.GradeCodedValueId!.Value))
                .Distinct()
                .ToHashSet();

            var codedValues = await settingsDb.CodedValues
                .AsNoTracking()
                .Where(cv => codedValueIds.Contains(cv.Id))
                .ToDictionaryAsync(cv => cv.Id, cv => new { cv.Name, cv.Code }, cancellationToken);

        logger.LogInformation("Resolved {Count} coded values from Settings DB", codedValues.Count);

        // For each assignment, run under the assignment's tenant, find-or-create
        // Subject/GradeLevel and update.
        var updated = 0;
        var errors = 0;

        foreach (var row in assignmentsToBackfill)
        {
            try
            {
                // §9.3 / Q-1: attribute to the assignment's tenant, or the System
                // tenant sink for legacy Guid.Empty assignments.
                var effectiveTenantId = row.TenantId == Guid.Empty
                    ? TenantSeeder.SystemTenantId
                    : row.TenantId;

                await tenantContextAccessor.RunWithExplicitTenantAsync(
                    effectiveTenantId,
                    async ct =>
                    {
                        Guid? subjectId = null;
                        Guid? gradeLevelId = null;

                        // Find-or-create Subject
                        if (row.SubjectCodedValueId.HasValue)
                        {
                            if (!codedValues.TryGetValue(row.SubjectCodedValueId.Value, out var cvInfo))
                            {
                                logger.LogWarning("Assignment {Id}: SubjectCodedValueId {CvId} not found in Settings DB; skipping",
                                    row.Id, row.SubjectCodedValueId.Value);
                                return false;
                            }

                            var subject = await studentsDb.Subjects
                                .FirstOrDefaultAsync(s => s.CodedValueId == row.SubjectCodedValueId.Value, ct);

                            if (subject is null)
                            {
                                subject = Subject.Create(
                                    codedValueId: row.SubjectCodedValueId.Value,
                                    code: cvInfo.Code,
                                    name: cvInfo.Name,
                                    displayOrder: 0);

                                await studentsDb.Subjects.AddAsync(subject, ct);
                                await studentsDb.SaveChangesAsync(ct);
                                logger.LogInformation("Created Subject {Id} from coded value {CvId} for tenant {TenantId}",
                                    subject.Id, row.SubjectCodedValueId.Value, effectiveTenantId);
                            }

                            subjectId = subject.Id;
                        }

                        // Find-or-create GradeLevel (if grade present)
                        if (row.GradeCodedValueId.HasValue)
                        {
                            if (!codedValues.TryGetValue(row.GradeCodedValueId.Value, out var cvInfo))
                            {
                                logger.LogWarning("Assignment {Id}: GradeCodedValueId {CvId} not found in Settings DB; skipping",
                                    row.Id, row.GradeCodedValueId.Value);
                                return false;
                            }

                            var gradeLevel = await studentsDb.GradeLevels
                                .FirstOrDefaultAsync(gl => gl.CodedValueId == row.GradeCodedValueId.Value, ct);

                            if (gradeLevel is null)
                            {
                                gradeLevel = GradeLevel.Create(
                                    codedValueId: row.GradeCodedValueId.Value,
                                    level: 0,
                                    name: cvInfo.Name,
                                    displayOrder: 0);

                                await studentsDb.GradeLevels.AddAsync(gradeLevel, ct);
                                await studentsDb.SaveChangesAsync(ct);
                                logger.LogInformation("Created GradeLevel {Id} from coded value {CvId} for tenant {TenantId}",
                                    gradeLevel.Id, row.GradeCodedValueId.Value, effectiveTenantId);
                            }

                            gradeLevelId = gradeLevel.Id;
                        }

                        // Raw-SQL update on Assignments DB
                        await using var updateCmd = assignmentsConnection.CreateCommand();
                        updateCmd.CommandText = @"
                            UPDATE assignments
                            SET subject_id = @subjectId, grade_level_id = @gradeLevelId
                            WHERE id = @id";

                        var subjectParam = updateCmd.CreateParameter();
                        subjectParam.ParameterName = "subjectId";
                        subjectParam.Value = subjectId ?? (object)DBNull.Value;
                        updateCmd.Parameters.Add(subjectParam);

                        var gradeParam = updateCmd.CreateParameter();
                        gradeParam.ParameterName = "gradeLevelId";
                        gradeParam.Value = gradeLevelId ?? (object)DBNull.Value;
                        updateCmd.Parameters.Add(gradeParam);

                        var idParam = updateCmd.CreateParameter();
                        idParam.ParameterName = "id";
                        idParam.Value = row.Id;
                        updateCmd.Parameters.Add(idParam);

                        await updateCmd.ExecuteNonQueryAsync(ct);
                        return true;
                    },
                    cancellationToken);

                updated++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to backfill assignment {Id}", row.Id);
                errors++;
            }
        }

        await assignmentsConnection.CloseAsync();

        logger.LogInformation("Assignment backfill complete: {Updated} updated, {Errors} errors", updated, errors);
    }

    private sealed record AssignmentBackfillRow(Guid Id, Guid? SubjectCodedValueId, Guid? GradeCodedValueId, Guid TenantId);
}
