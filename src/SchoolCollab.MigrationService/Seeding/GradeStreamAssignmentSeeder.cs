using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Backfills the grade↔stream bridge (<see cref="GradeStreamAssignment"/>) — the
/// link that replaced the coded value's <c>gradeLevel</c> attribute.
/// </summary>
/// <remarks>
/// <para><b>Why a seeder, not a migration.</b> The coded values live physically in
/// the Settings database; the bridge lives in the Students database. They are two
/// separate Npgsql databases registered in one DI scope here, so no EF migration on
/// either could read the other. This seeder runs in the one scope that has both
/// contexts, strictly AFTER the Settings migration + coded-value seed and AFTER the
/// Students migration (see <c>Program.cs</c>).</para>
/// <para><b>Preservation only — it never materializes a grade level (owner decisions
/// "configured grades only" + "manual linking").</b> The single source is the
/// historical <c>gradeLevel</c> attribute still stored on a GRSTREAMS child of an
/// upgraded database. A pair is linked only when the tenant ALREADY has a
/// <see cref="GradeLevel"/> row for that grade; the seeder never creates one. So the
/// grade-level landing keeps meaning "the grades this school configured", and a newly
/// configured grade gets no streams automatically — the school adds them through the
/// Streams card. A fresh database therefore seeds no bridge rows at all; the native
/// <c>seed-stream-assignments.csv</c> mapping was deleted with this change.</para>
/// <para><b>Visibility.</b> A global (NULL-tenant) stream is offered for every
/// registered tenant; a tenant-owned stream only for its own tenant — mirroring the
/// resolved-value visibility of the coded-value read path.</para>
/// <para><b>Idempotent.</b> Bridge rows are keyed by
/// <c>(tenant_id, grade_level_id, stream_coded_value_id)</c> — a re-run inserts
/// nothing.</para>
/// </remarks>
public sealed class GradeStreamAssignmentSeeder(
    SettingsDbContext settingsDb,
    StudentsDbContext studentsDb,
    ITenantContextAccessor tenantContextAccessor,
    ILogger<GradeStreamAssignmentSeeder> logger)
{
    /// <summary>The legacy attribute that used to carry the grade↔stream link.</summary>
    public const string LegacyGradeLevelAttributeKey = "gradeLevel";

    private const string StreamsParentCode = GradeStreamAssignment.CatalogueParentCode;
    private const string GradeParentCode = "GRADE";

    /// <summary>
    /// Backfills the bridge for every registered tenant. Returns the number of
    /// bridge rows inserted (0 on a re-run, or when no tenant has the grade).
    /// </summary>
    /// <param name="tenantIdsByName">
    /// Tenant registry keyed by name, as returned by <see cref="TenantSeeder.SeedAsync"/> —
    /// the same delegate-the-map shape <see cref="PilotActivityGroupFlagOverrideSeeder"/>
    /// uses. An empty map skips the pass (there is nothing to attribute rows to).
    /// </param>
    public async Task<int> SeedAsync(
        IReadOnlyDictionary<string, Guid> tenantIdsByName,
        CancellationToken ct = default)
    {
        if (tenantIdsByName.Count == 0)
        {
            logger.LogWarning("No registered tenants; skipping grade↔stream backfill");
            return 0;
        }

        // The seeder runs under the default (Guid.Empty) context and writes rows for
        // real tenants, so the strict save-guard is suppressed for the whole pass —
        // the sanctioned seed bypass (global-tenant-filter.md §12 Step 5).
        using (tenantContextAccessor.SuppressTenantGuard())
        {
            return await SeedCoreAsync(tenantIdsByName.Values.Distinct().ToArray(), ct);
        }
    }

    private async Task<int> SeedCoreAsync(Guid[] tenantIds, CancellationToken ct)
    {
        var codedValues = await settingsDb.CodedValues
            .IgnoreQueryFilters(["Tenant"])
            .Include(x => x.Attributes)
            .AsNoTracking()
            .ToListAsync(ct);

        var streamsParentId = codedValues
            .FirstOrDefault(x => x.Code == StreamsParentCode && x.TenantId is null)?.Id;
        var gradeParentId = codedValues
            .FirstOrDefault(x => x.Code == GradeParentCode && x.TenantId is null)?.Id;

        if (streamsParentId is null || gradeParentId is null)
        {
            logger.LogWarning(
                "GRSTREAMS/GRADE coded-value parents not found; skipping grade↔stream backfill");
            return 0;
        }

        // Grade code → coded value id (the shared blueprint row).
        var gradeCodedValueByCode = codedValues
            .Where(x => x.ParentId == gradeParentId && x.TenantId is null && !x.IsDeleted)
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Every GRSTREAMS child, kept with its owning tenant (null = shared blueprint).
        var streamRows = codedValues
            .Where(x => x.ParentId == streamsParentId && !x.IsDeleted)
            .ToList();

        var intendedPairs = ResolveIntendedPairs(streamRows, gradeCodedValueByCode.Values.ToList());

        if (intendedPairs.Count == 0)
        {
            logger.LogInformation("No stored gradeLevel attributes to backfill; nothing to insert");
            return 0;
        }

        // ── Existing state (cross-tenant reads, so the "Tenant" filter is ignored) ──
        // The grade_levels read is a LOOKUP, never a materialization: a pair whose
        // tenant has no row for the grade is skipped below.
        var existingGradeLevels = await studentsDb.GradeLevels
            .IgnoreQueryFilters(["Tenant"])
            .AsNoTracking()
            .Select(x => new { x.Id, x.TenantId, x.CodedValueId })
            .ToListAsync(ct);
        var gradeLevelIdByKey = existingGradeLevels
            .GroupBy(x => (x.TenantId, x.CodedValueId))
            .ToDictionary(g => g.Key, g => g.First().Id);

        var existingAssignments = await studentsDb.GradeStreamAssignments
            .IgnoreQueryFilters(["Tenant"])
            .AsNoTracking()
            .Select(x => new { x.TenantId, x.GradeLevelId, x.StreamCodedValueId })
            .ToListAsync(ct);
        var existingKeys = existingAssignments
            .Select(x => (x.TenantId, x.GradeLevelId, x.StreamCodedValueId))
            .ToHashSet();

        var pendingAssignments = new List<GradeStreamAssignment>();
        var skippedNoGrade = 0;

        foreach (var (streamCode, gradeCode) in intendedPairs)
        {
            if (!gradeCodedValueByCode.TryGetValue(gradeCode, out var gradeCodedValue))
            {
                logger.LogWarning(
                    "Grade coded value {GradeCode} not found; skipping its stream assignments", gradeCode);
                continue;
            }

            // A stream code can be held by both a shared blueprint row (offered for
            // every tenant) and a tenant-owned row (offered for that tenant only).
            foreach (var stream in streamRows.Where(x =>
                         string.Equals(x.Code, streamCode, StringComparison.OrdinalIgnoreCase)))
            {
                var targetTenants = stream.TenantId is { } owner
                    ? (tenantIds.Contains(owner) ? [owner] : [])
                    : tenantIds;

                foreach (var tenantId in targetTenants)
                {
                    // NEVER materialize a grade level: link only against a row the
                    // tenant already has. This is what keeps the grade-level landing
                    // to "configured grades only".
                    if (!gradeLevelIdByKey.TryGetValue((tenantId, gradeCodedValue.Id), out var gradeLevelId))
                    {
                        skippedNoGrade++;
                        continue;
                    }

                    if (!existingKeys.Add((tenantId, gradeLevelId, stream.Id))) continue;

                    pendingAssignments.Add(
                        GradeStreamAssignment.Create(gradeLevelId, stream.Id).WithTenant(tenantId));
                }
            }
        }

        if (pendingAssignments.Count == 0)
        {
            logger.LogInformation(
                "Grade↔stream bridge already backfilled; nothing to insert ({Skipped} pair(s) skipped — their grade level does not exist for the tenant)",
                skippedNoGrade);
            return 0;
        }

        await using var tx = await studentsDb.Database.BeginTransactionAsync(ct);
        try
        {
            studentsDb.GradeStreamAssignments.AddRange(pendingAssignments);
            await studentsDb.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        logger.LogInformation(
            "Seeded {AssignmentCount} grade↔stream assignment(s); {Skipped} pair(s) skipped (grade level absent for the tenant)",
            pendingAssignments.Count, skippedNoGrade);
        return pendingAssignments.Count;
    }

    /// <summary>
    /// The stored legacy <c>gradeLevel</c> attributes still present on GRSTREAMS
    /// children — the ONLY bridge source. A fresh database (whose attribute rows were
    /// removed from the seed) yields no pairs, so it seeds no bridge rows.
    /// </summary>
    private HashSet<(string StreamCode, string GradeCode)> ResolveIntendedPairs(
        IReadOnlyList<CodedValue> streamRows,
        IReadOnlyList<CodedValue> gradeCodedValues)
    {
        var pairs = new HashSet<(string, string)>();
        var gradeCodeById = gradeCodedValues
            .GroupBy(x => x.Id)
            .ToDictionary(g => g.Key, g => g.First().Code);

        foreach (var stream in streamRows)
        {
            var attribute = stream.Attributes
                .FirstOrDefault(a => a.Key == LegacyGradeLevelAttributeKey);
            if (attribute is null) continue;

            if (!Guid.TryParse(attribute.Value, out var gradeCodedValueId)
                || !gradeCodeById.TryGetValue(gradeCodedValueId, out var gradeCode))
            {
                logger.LogWarning(
                    "GRSTREAMS child {StreamCode} carries a gradeLevel value '{Value}' that resolves to no GRADE coded value; skipping",
                    stream.Code, attribute.Value);
                continue;
            }

            pairs.Add((stream.Code, gradeCode));
        }

        return pairs;
    }
}
