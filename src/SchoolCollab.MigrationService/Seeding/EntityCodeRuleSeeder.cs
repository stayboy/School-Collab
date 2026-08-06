using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Seeds the default <see cref="EntityCodeRule"/> rows (shared blueprints,
/// <c>TenantId = null</c>) and their <see cref="EntityCodeSegment"/> children for
/// student, staff, and assignment code generation (spec §3.7). Idempotent: skips
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
            await SeedRuleAsync(
                "STUDENT_CODE",
                "Student Code Template",
                "Auto-generates student student numbers",
                stamp: "STU",
                ct);
            await SeedRuleAsync(
                "STAFF_CODE",
                "Staff Code Template",
                "Auto-generates staff staff numbers",
                stamp: "STF",
                ct);
            await SeedRuleAsync(
                "ASSIGNMENT_CODE",
                "Assignment Code Template",
                "Auto-generates assignment numbers",
                stamp: "ASG",
                ct);
            await SeedTopicRuleAsync(ct);
        }
    }

    /// <summary>
    /// Seeds the <c>TOPIC_CODE</c> template: a <see cref="SegmentType.WordInitials"/>
    /// segment (initials derived from the topic name, e.g. "computer science" → "CS")
    /// followed by a <see cref="SegmentType.NumericSequence"/> (zero-padded, e.g. "01"),
    /// producing codes like <c>CS01</c> (tcv/2).
    /// </summary>
    private async Task SeedTopicRuleAsync(CancellationToken ct)
    {
        const string code = "TOPIC_CODE";
        var existing = await db.EntityCodeRules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Code == code, ct);
        if (existing is not null)
        {
            logger.LogDebug("EntityCodeRule {Code} already exists. Skipping", code);
            return;
        }

        var rule = EntityCodeRule.Create(
            code,
            "Topic Code Template",
            "Auto-generates topic codes from the topic name initials + a number",
            isActive: true);
        rule.AddSegment(EntityCodeSegment.Sequence(0, null, SegmentType.WordInitials, minWidth: 1));
        rule.AddSegment(EntityCodeSegment.Sequence(1, null, SegmentType.NumericSequence, minWidth: 2));

        db.EntityCodeRules.Add(rule);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded EntityCodeRule {Code} (word-initials + numeric)", code);
    }

    private async Task SeedRuleAsync(string code, string name, string description, string stamp, CancellationToken ct)
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

        var rule = EntityCodeRule.Create(code, name, description, isActive: true);
        // Shared blueprint: NULL tenant (the default from Create; visible to all
        // tenants via the hybrid filter). The guard is suppressed for this seed pass.

        rule.AddSegment(EntityCodeSegment.Fixed(0, "stamp", stamp));
        rule.AddSegment(EntityCodeSegment.Sequence(
            index: 1,
            role: null,
            type: SegmentType.AlphanumericSequence,
            prefix: "A",
            minWidth: 2,
            upperLimit: "09"));

        db.EntityCodeRules.Add(rule);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded EntityCodeRule {Code} (stamp {Stamp})", normalised, stamp);
    }
}