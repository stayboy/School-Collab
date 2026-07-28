using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Repositories;

/// <summary>
/// Repository for <see cref="TenantEntityCodeRuleOverride"/> rows (spec §4.12).
/// Strict tenant scoped — the query filter from
/// <c>TenantEntityCodeRuleOverrideConfiguration</c> already isolates rows
/// by the current tenant, so these methods do not take a tenant id parameter.
/// </summary>
public interface ITenantEntityCodeRuleOverrideRepository
{
    /// <summary>
    /// Returns all overrides the current tenant has on the given rule, ordered
    /// by (segment id, field). Used by the generator to layer overrides on
    /// top of the rule's segments, and by the admin UI to show / edit them.
    /// </summary>
    Task<List<TenantEntityCodeRuleOverride>> ListForRuleAsync(
        Guid generationRuleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the entire override set for the current tenant on the given
    /// rule with <paramref name="overrides"/>. Used by the
    /// PUT /api/entity-code-rules/{id}/overrides endpoint — the admin sends
    /// the full ordered list and we replace atomically.
    /// </summary>
    Task ReplaceForRuleAsync(
        Guid generationRuleId,
        IReadOnlyList<TenantEntityCodeRuleOverride> overrides,
        CancellationToken cancellationToken = default);
}
