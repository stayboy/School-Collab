using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.Services;

/// <summary>
/// Default <see cref="IEntityCodeGenerator"/>. Loads the active
/// <see cref="EntityCodeRule"/> for the requested code, advances every segment for
/// the current period (<see cref="EntityCodeRule.GenerateNext"/>), and persists the
/// per-segment sequence state atomically.
/// <para>
/// <b>Concurrency</b> (spec §4.3): each attempt uses a short-lived
/// <see cref="SettingsDbContext"/> from the factory and relies on the rule row's
/// PostgreSQL <c>xmin</c> optimistic-concurrency token. If another writer advanced
/// the same rule since it was loaded, <c>SaveChangesAsync</c> throws
/// <see cref="DbUpdateConcurrencyException"/> and the generator retries with a fresh
/// context (up to <see cref="MaxAttempts"/> attempts).
/// </para>
/// </summary>
public sealed class EntityCodeGenerator(
    IDbContextFactory<SettingsDbContext> dbFactory,
    ITenantProvider tenantProvider,
    ILogger<EntityCodeGenerator> logger) : IEntityCodeGenerator
{
    private const int MaxAttempts = 3;

    public async Task<string> GenerateAsync(string ruleCode, CancellationToken cancellationToken = default)
    {
        var normalised = ruleCode.Trim().ToUpperInvariant();
        var attempt = 0;

        while (true)
        {
            attempt++;
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            // Find the active rule by Code. The hybrid tenant filter
            // (TenantId == CurrentTenantId OR TenantId == null) would let a
            // tenant see both their own rule and the shared blueprint for the
            // same Code, which would make SingleOrDefaultAsync throw. This is
            // safe because EntityCodeRuleConfiguration declares a GLOBALLY
            // unique index on Code (ix_entity_code_rules_code_unique) — only
            // one row per Code can exist across all tenants, so there is never
            // ambiguity. If that index is ever relaxed, this query must change
            // (e.g. prefer the tenant-owned row, fall back to the shared
            // blueprint) to avoid an InvalidOperationException.
            var rule = await db.EntityCodeRules
                .SingleOrDefaultAsync(x => x.Code == normalised && x.IsActive, cancellationToken);

            if (rule is null)
                throw new EntityCodeRuleNotFoundException(normalised);

            // Tenant overrides only apply when the active rule is the SHARED
            // blueprint (TenantId == null). A tenant-owned active rule already
            // carries the tenant's own segments — applying an override table
            // on top would double-apply. We still load the table (cheap) but
            // skip it when the active rule is tenant-owned. The default
            // tenant (Guid.Empty) is treated like a real tenant here: it
            // owns overrides targeting the shared blueprint.
            var currentTenantId = tenantProvider.GetTenantContext().TenantId;
            var overridesBySegment = rule.TenantId is null
                ? await LoadOverridesAsync(db, rule.Id, currentTenantId, cancellationToken)
                : new Dictionary<Guid, IReadOnlyDictionary<OverrideField, string>>();

            var now = DateTimeOffset.UtcNow;
            string code;
            try
            {
                code = overridesBySegment.Count == 0
                    ? rule.GenerateNext(now)
                    : rule.GenerateNextWithOverrides(now, overridesBySegment);
            }
            catch (EntityCodeGenerationCollisionException ex)
            {
                // The sequence hit its upper limit and cannot roll over. This is not
                // transient — rethrow immediately so the caller surfaces it.
                logger.LogError(ex, "Entity code generation collision for rule {RuleCode}", normalised);
                throw;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogDebug("Generated entity code {Code} for rule {RuleCode} (attempt {Attempt})",
                    code, normalised, attempt);
                return code;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // xmin changed since load — another writer advanced the rule first.
                // Discard this context and retry with a fresh load.
                logger.LogWarning(ex,
                    "Concurrency conflict advancing rule {RuleCode} (attempt {Attempt}/{Max}); retrying",
                    normalised, attempt, MaxAttempts);

                if (attempt >= MaxAttempts)
                    throw new ConcurrencyException(rule.Id);

                // Loop reloads the rule under a new context.
            }
        }
    }

    /// <summary>
    /// Loads the current tenant's overrides for <paramref name="ruleId"/> and
    /// groups them by segment id → field → value. Uses the same DbContext as
    /// the rule load so they share the same transaction (and the read is
    /// fresh — no risk of overrides being added mid-generation).
    /// </summary>
    private static async Task<Dictionary<Guid, IReadOnlyDictionary<OverrideField, string>>> LoadOverridesAsync(
        SettingsDbContext db,
        Guid ruleId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // Empty tenant (default sentinel) has no overrides — saves a query
        // and a meaningful behaviour: dev affordance shouldn't accidentally
        // pick up a stale override table.
        if (tenantId == Guid.Empty)
            return new Dictionary<Guid, IReadOnlyDictionary<OverrideField, string>>();

        var rows = await db.TenantEntityCodeRuleOverrides
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.GenerationRuleId == ruleId)
            .ToListAsync(cancellationToken);

        var grouped = new Dictionary<Guid, IReadOnlyDictionary<OverrideField, string>>(rows.Count);
        foreach (var row in rows)
        {
            if (!grouped.TryGetValue(row.EntityCodeSegmentId, out var fields))
            {
                fields = new Dictionary<OverrideField, string>();
                grouped[row.EntityCodeSegmentId] = fields;
            }
            ((Dictionary<OverrideField, string>)fields)[row.Field] = row.Value;
        }
        return grouped;
    }
}