using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Seeds the default <see cref="EntityCodeRule"/> rows (shared blueprints,
/// <c>TenantId = null</c>) and their <see cref="EntityCodeSegment"/> children for
/// student, staff, assignment, topic and notification-template code generation (spec §3.7;
/// naming spec D5 for the template rule). Idempotent: skips
/// rules whose <c>Code</c> already exists. Mirrors the
/// <see cref="CodedValueSeeder"/> bypass pattern (guard suppressed for the seed
/// pass — global-tenant-filter.md §12 Step 5).
/// </summary>
public sealed class EntityCodeRuleSeeder(
    SettingsDbContext db,
    ITenantContextAccessor tenantContextAccessor,
    ILogger<EntityCodeRuleSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        using (tenantContextAccessor.SuppressTenantGuard())
        {
            // The four numbered rules share one shape: a fixed stamp, then an alphanumeric sequence
            // whose prefix starts at "A" and whose two-digit ceiling is "09".
            await SeedRuleAsync(
                "STUDENT_CODE",
                "Student Code Template",
                "Auto-generates student student numbers",
                [
                    EntityCodeSegment.Fixed(0, "stamp", "STU"),
                    EntityCodeSegment.Sequence(
                        1, null, SegmentType.AlphanumericSequence, prefix: "A", minWidth: 2, upperLimit: "09"),
                ],
                ct);
            await SeedRuleAsync(
                "STAFF_CODE",
                "Staff Code Template",
                "Auto-generates staff staff numbers",
                [
                    EntityCodeSegment.Fixed(0, "stamp", "STF"),
                    EntityCodeSegment.Sequence(
                        1, null, SegmentType.AlphanumericSequence, prefix: "A", minWidth: 2, upperLimit: "09"),
                ],
                ct);
            await SeedRuleAsync(
                "ASSIGNMENT_CODE",
                "Assignment Code Template",
                "Auto-generates assignment numbers",
                [
                    EntityCodeSegment.Fixed(0, "stamp", "ASG"),
                    EntityCodeSegment.Sequence(
                        1, null, SegmentType.AlphanumericSequence, prefix: "A", minWidth: 2, upperLimit: "09"),
                ],
                ct);

            // A WordInitials segment (initials derived from the topic name, e.g. "computer science" →
            // "CS") followed by a zero-padded numeric sequence, producing codes like "CS01" (tcv/2).
            await SeedRuleAsync(
                "TOPIC_CODE",
                "Topic Code Template",
                "Auto-generates topic codes from the topic name initials + a number",
                [
                    EntityCodeSegment.Sequence(0, null, SegmentType.WordInitials, minWidth: 1),
                    EntityCodeSegment.Sequence(1, null, SegmentType.NumericSequence, minWidth: 2),
                ],
                ct);

            // Naming spec D5: the template rule is a fixed "TPL" stamp plus a zero-padded numeric
            // sequence (no period reset), producing TPL01, TPL02, … TPL100.
            //
            // The sequence carries an explicit 999 upper limit: a numeric segment with no upper limit
            // caps at 10^MinWidth - 1, so a two-digit width alone would collide at the hundredth
            // template. Width and limit are separate knobs — the width still pads to two digits
            // (TPL07), the limit lets the hundredth row through (TPL100).
            await SeedRuleAsync(
                TenantNotificationTemplate.CodeRule,
                "Notification Template Code Template",
                "Auto-generates notification template codes (TPL01, TPL02, …)",
                [
                    EntityCodeSegment.Fixed(0, "stamp", "TPL"),
                    EntityCodeSegment.Sequence(
                        1, null, SegmentType.NumericSequence, minWidth: 2, upperLimit: "999"),
                ],
                ct);
        }
    }

    /// <summary>
    /// Seeds one shared-blueprint rule unless a rule with the same code exists: lookup, skip, create,
    /// add the rule's segments, save, log. <paramref name="segments"/> is the only part that differs
    /// between rules, so every call site states its rule's shape and nothing else.
    /// </summary>
    private async Task SeedRuleAsync(
        string code,
        string name,
        string description,
        IReadOnlyList<EntityCodeSegment> segments,
        CancellationToken ct)
    {
        var normalised = code.Trim().ToUpperInvariant();
        var existing = await db.EntityCodeRules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Code == normalised, ct);

        if (existing is not null)
        {
            logger.LogDebug("EntityCodeRule {Code} already exists. Skipping", normalised);
            return;
        }

        var rule = EntityCodeRule.Create(normalised, name, description, isActive: true);
        // Shared blueprint: NULL tenant (the default from Create; visible to all tenants via the hybrid
        // filter). The guard is suppressed for this seed pass.
        foreach (var segment in segments)
            rule.AddSegment(segment);

        db.EntityCodeRules.Add(rule);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded EntityCodeRule {Code} ({SegmentCount} segments)", normalised, segments.Count);
    }
}
