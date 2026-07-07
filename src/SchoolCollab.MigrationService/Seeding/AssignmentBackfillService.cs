using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Backfills SubjectId and GradeLevelId on Assignments from legacy coded-value IDs.
/// This is a one-time migration step for PR 4 of the grade-level-setup spec.
///
/// The migration that added SubjectId/GradeLevelId columns kept the old
/// subject_coded_value_id and grade_coded_value_id columns for this backfill.
/// After this runs successfully in all environments, a future cleanup migration
/// can drop the old columns.
///
/// Process:
/// 1. Raw-SQL read from Assignments DB: assignments where subject_id IS NULL
/// 2. For each: look up coded value in Settings DB by id
/// 3. Find-or-create Subject in Students DB by CodedValueId
/// 4. If grade coded value present: find-or-create GradeLevel by CodedValueId
/// 5. Raw-SQL update assignments SET subject_id=@s, grade_level_id=@g WHERE id=@id
///
/// Dev DB has no assignments → backfill is a no-op in dev.
/// Hard to unit-test (raw SQL across 3 databases) → document it.
/// </summary>
public sealed class AssignmentBackfillService(
    SettingsDbContext settingsDb,
    StudentsDbContext studentsDb,
    AssignmentsDbContext assignmentsDb,
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
                SELECT id, subject_coded_value_id, grade_coded_value_id
                FROM assignments
                WHERE subject_id IS NULL";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                var subjectCodedValueId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
                var gradeCodedValueId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
                assignmentsToBackfill.Add(new AssignmentBackfillRow(id, subjectCodedValueId, gradeCodedValueId));
            }
        }

        if (assignmentsToBackfill.Count == 0)
        {
            logger.LogInformation("No assignments require backfill; exiting");
            return;
        }

        logger.LogInformation("Found {Count} assignments to backfill", assignmentsToBackfill.Count);

        // Batch lookup coded values from Settings DB
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

        // For each assignment, find-or-create Subject/GradeLevel and update
        var updated = 0;
        var errors = 0;

        foreach (var row in assignmentsToBackfill)
        {
            try
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
                        continue;
                    }

                    var subject = await studentsDb.Subjects
                        .FirstOrDefaultAsync(s => s.CodedValueId == row.SubjectCodedValueId.Value, cancellationToken);

                    if (subject is null)
                    {
                        // Create Subject with placeholder Level=0
                        subject = Subject.Create(
                            codedValueId: row.SubjectCodedValueId.Value,
                            code: cvInfo.Code,
                            name: cvInfo.Name,
                            displayOrder: 0);

                        await studentsDb.Subjects.AddAsync(subject, cancellationToken);
                        await studentsDb.SaveChangesAsync(cancellationToken);
                        logger.LogInformation("Created Subject {Id} from coded value {CvId}", subject.Id, row.SubjectCodedValueId.Value);
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
                        continue;
                    }

                    var gradeLevel = await studentsDb.GradeLevels
                        .FirstOrDefaultAsync(gl => gl.CodedValueId == row.GradeCodedValueId.Value, cancellationToken);

                    if (gradeLevel is null)
                    {
                        // Create GradeLevel with placeholder Level=0
                        gradeLevel = GradeLevel.Create(
                            codedValueId: row.GradeCodedValueId.Value,
                            level: 0,
                            name: cvInfo.Name,
                            displayOrder: 0);

                        await studentsDb.GradeLevels.AddAsync(gradeLevel, cancellationToken);
                        await studentsDb.SaveChangesAsync(cancellationToken);
                        logger.LogInformation("Created GradeLevel {Id} from coded value {CvId}", gradeLevel.Id, row.GradeCodedValueId.Value);
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

                await updateCmd.ExecuteNonQueryAsync(cancellationToken);
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

    private sealed record AssignmentBackfillRow(Guid Id, Guid? SubjectCodedValueId, Guid? GradeCodedValueId);
}