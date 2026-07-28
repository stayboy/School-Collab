using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Repositories;

internal sealed class TenantEntityCodeRuleOverrideRepository(
    SettingsDbContext db,
    ITenantProvider tenantProvider) : ITenantEntityCodeRuleOverrideRepository
{
    public Task<List<TenantEntityCodeRuleOverride>> ListForRuleAsync(
        Guid generationRuleId,
        CancellationToken cancellationToken = default) =>
        db.TenantEntityCodeRuleOverrides
            .Where(o => o.GenerationRuleId == generationRuleId)
            .OrderBy(o => o.EntityCodeSegmentId)
            .ThenBy(o => o.Field)
            .ToListAsync(cancellationToken);

    public async Task ReplaceForRuleAsync(
        Guid generationRuleId,
        IReadOnlyList<TenantEntityCodeRuleOverride> overrides,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException(
                "Cannot replace tenant overrides without a resolved tenant context.");

        // Load existing rows for this (tenant, rule) inside the same transaction
        // so a concurrent edit doesn't get clobbered.
        var existing = await db.TenantEntityCodeRuleOverrides
            .Where(o => o.GenerationRuleId == generationRuleId)
            .ToListAsync(cancellationToken);

        var incomingIds = overrides.Select(o => o.Id).Where(id => id != Guid.Empty).ToHashSet();

        // Delete any existing row that is NOT in the incoming set (full replace).
        foreach (var row in existing)
        {
            if (!incomingIds.Contains(row.Id))
                db.TenantEntityCodeRuleOverrides.Remove(row);
        }

        // Apply or insert each incoming row. We rely on the unique index to
        // detect duplicate (segment, field) pairs in the incoming list — the
        // handler is responsible for client-side validation, but the DB
        // backstops it.
        foreach (var incoming in overrides)
        {
            var match = existing.FirstOrDefault(e => e.Id == incoming.Id);
            if (match is not null)
            {
                match.UpdateValue(incoming.Value);
            }
            else
            {
                db.TenantEntityCodeRuleOverrides.Add(incoming);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
