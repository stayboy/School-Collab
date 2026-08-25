using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Settings.Core.CQRS.FeatureFlags.Commands;

public sealed class CreateFeatureFlagHandler(
    SettingsDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<CreateFeatureFlag, Guid>
{
    public async Task<Guid> HandleAsync(CreateFeatureFlag command, CancellationToken ct = default)
    {
        var normalized = FeatureFlag.NormalizeKey(command.Key);

        if (await db.FeatureFlags.AnyAsync(f => f.Key == normalized && !f.IsDeleted, ct))
        {
            throw new InvalidOperationException($"Feature flag '{normalized}' already exists.");
        }

        var flag = FeatureFlag.Create(command.Key, command.Name, command.Description, command.IsEnabled);
        db.FeatureFlags.Add(flag);
        auditor.Record(db, tenantId: null, flag.Id, flag.Key, FlagChangeKind.Created,
            previousIsEnabled: null, newIsEnabled: flag.IsEnabled, command.Reason);
        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, TenantId: null, nameof(FlagChangeKind.Created), flag.IsEnabled, flag.CreatedAt), ct);

        await db.SaveChangesAsync(ct);

        return flag.Id;
    }
}

public sealed class RenameFeatureFlagHandler(
    SettingsDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<RenameFeatureFlag>
{
    public async Task HandleAsync(RenameFeatureFlag command, CancellationToken ct = default)
    {
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, command.Key, ct);
        flag.Rename(command.Name, command.Description);
        auditor.Record(db, tenantId: null, flag.Id, flag.Key, FlagChangeKind.Renamed,
            previousIsEnabled: flag.IsEnabled, newIsEnabled: flag.IsEnabled, command.Reason);
        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, nameof(FlagChangeKind.Renamed), flag.IsEnabled, flag.UpdatedAt), ct);

        await db.SaveChangesAsync(ct);
    }
}

public sealed class SetFeatureFlagEnabledHandler(
    SettingsDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<SetFeatureFlagEnabled>
{
    public async Task HandleAsync(SetFeatureFlagEnabled command, CancellationToken ct = default)
    {
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, command.Key, ct);
        var previous = flag.IsEnabled;
        flag.SetEnabled(command.IsEnabled);

        if (previous == flag.IsEnabled)
        {
            return; // no-op; nothing to audit or publish
        }

        var kind = flag.IsEnabled ? FlagChangeKind.Enabled : FlagChangeKind.Disabled;
        auditor.Record(db, tenantId: null, flag.Id, flag.Key, kind,
            previousIsEnabled: previous, newIsEnabled: flag.IsEnabled, command.Reason);
        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, kind.ToString(), flag.IsEnabled, flag.UpdatedAt), ct);

        await db.SaveChangesAsync(ct);
    }
}

public sealed class ArchiveFeatureFlagHandler(
    SettingsDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<ArchiveFeatureFlag>
{
    public async Task HandleAsync(ArchiveFeatureFlag command, CancellationToken ct = default)
    {
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, command.Key, ct);
        flag.Archive();
        auditor.Record(db, null, flag.Id, flag.Key, FlagChangeKind.Archived,
            flag.IsEnabled, flag.IsEnabled, command.Reason);
        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, nameof(FlagChangeKind.Archived), flag.IsEnabled, flag.UpdatedAt), ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class UnarchiveFeatureFlagHandler(
    SettingsDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<UnarchiveFeatureFlag>
{
    public async Task HandleAsync(UnarchiveFeatureFlag command, CancellationToken ct = default)
    {
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, command.Key, ct);
        flag.Unarchive();
        auditor.Record(db, null, flag.Id, flag.Key, FlagChangeKind.Unarchived,
            flag.IsEnabled, flag.IsEnabled, command.Reason);
        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, nameof(FlagChangeKind.Unarchived), flag.IsEnabled, flag.UpdatedAt), ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class DeleteFeatureFlagHandler(
    SettingsDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<DeleteFeatureFlag>
{
    public async Task HandleAsync(DeleteFeatureFlag command, CancellationToken ct = default)
    {
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, command.Key, ct);
        flag.Delete();
        auditor.Record(db, null, flag.Id, flag.Key, FlagChangeKind.Deleted,
            flag.IsEnabled, flag.IsEnabled, command.Reason);
        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, nameof(FlagChangeKind.Deleted), flag.IsEnabled, flag.UpdatedAt), ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class RecoverFeatureFlagHandler(
    SettingsDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<RecoverFeatureFlag>
{
    public async Task HandleAsync(RecoverFeatureFlag command, CancellationToken ct = default)
    {
        var flag = await db.FeatureFlags.SingleOrDefaultAsync(f => f.Key == FeatureFlag.NormalizeKey(command.Key), ct)
            ?? throw new KeyNotFoundException($"Feature flag '{command.Key}' not found.");
        flag.Recover();
        auditor.Record(db, null, flag.Id, flag.Key, FlagChangeKind.Recovered,
            flag.IsEnabled, flag.IsEnabled, command.Reason);
        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, nameof(FlagChangeKind.Recovered), flag.IsEnabled, flag.UpdatedAt), ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class UpsertTenantFlagOverrideHandler(
    SettingsDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher,
    ITenantContextAccessor tenantContext) : ICommandHandler<UpsertTenantFlagOverride, TenantFlagOverrideDto>
{
    public async Task<TenantFlagOverrideDto> HandleAsync(UpsertTenantFlagOverride command, CancellationToken ct = default)
    {
        var key = FeatureFlag.NormalizeKey(command.Key);
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, key, ct);

        var existing = await db.TenantFlagOverrides
            .IgnoreQueryFilters(["Tenant"])
            .SingleOrDefaultAsync(o => o.TenantId == command.TenantId && o.FeatureFlagId == flag.Id && !o.IsDeleted, ct);

        FlagChangeKind kind = default;
        bool? previous = null;

        // FR-6: the override row is owned by command.TenantId, which may differ from the
        // ambient (admin) context. Run the write under the target tenant so the save-guard
        // (PrepareChanges) accepts the cross-tenant write. See global-tenant-filter.md §override.
        var dto = await tenantContext.RunWithExplicitTenantAsync(command.TenantId, async ct2 =>
        {
            if (existing is null)
            {
                var created = TenantFeatureFlagOverride.Create(
                    command.TenantId, flag.Id, command.IsEnabled, command.Reason, command.EffectiveFrom, command.EffectiveTo);
                db.TenantFlagOverrides.Add(created);
                existing = created;
                kind = FlagChangeKind.OverrideCreated;
            }
            else
            {
                previous = existing.IsEnabled;
                existing.Update(command.IsEnabled, command.Reason, command.EffectiveFrom, command.EffectiveTo);
                kind = FlagChangeKind.OverrideUpdated;
            }

            auditor.Record(db, command.TenantId, flag.Id, flag.Key, kind,
                previousIsEnabled: previous, newIsEnabled: command.IsEnabled, command.Reason);

            // Enqueue BEFORE save: atomic commit with the override. The explicit
            // null tenant stamp keeps the outbox row GLOBAL (feature-flag changes
            // are global config) even though the write runs under the target
            // tenant's save-guard — same routing as the other handlers.
            await publisher.EnqueueAsync(new FeatureFlagChanged(
                flag.Id, flag.Key, command.TenantId, kind.ToString(), command.IsEnabled, existing.UpdatedAt),
                tenantStamp: null, ct);

            await db.SaveChangesAsync(ct2);

            return FeatureFlagCommandHelpers.ToDto(existing);
        }, ct);

        return dto;
    }
}

public sealed class DeleteTenantFlagOverrideHandler(
    SettingsDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher,
    ITenantContextAccessor tenantContext) : ICommandHandler<DeleteTenantFlagOverride>
{
    public async Task HandleAsync(DeleteTenantFlagOverride command, CancellationToken ct = default)
    {
        var key = FeatureFlag.NormalizeKey(command.Key);
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, key, ct);

        var existing = await db.TenantFlagOverrides
            .IgnoreQueryFilters(["Tenant"])
            .SingleOrDefaultAsync(o => o.TenantId == command.TenantId && o.FeatureFlagId == flag.Id && !o.IsDeleted, ct)
            ?? throw new KeyNotFoundException($"Tenant override for flag '{key}' and tenant {command.TenantId} not found.");

        var previous = existing.IsEnabled;

        // FR-6: the override row is owned by command.TenantId (possibly != ambient context).
        // Run the soft-delete under the target tenant so the save-guard accepts it.
        await tenantContext.RunWithExplicitTenantAsync(command.TenantId, async ct2 =>
        {
            existing.MarkAsDeleted();
            auditor.Record(db, command.TenantId, flag.Id, flag.Key, FlagChangeKind.OverrideDeleted,
                previousIsEnabled: previous, newIsEnabled: null, command.Reason);

            // Enqueue BEFORE save: atomic with the removal; explicit null stamp keeps
            // the outbox row global (see UpsertTenantFlagOverrideHandler).
            await publisher.EnqueueAsync(new FeatureFlagChanged(
                flag.Id, flag.Key, command.TenantId, nameof(FlagChangeKind.OverrideDeleted), NewIsEnabled: null, existing.UpdatedAt),
                tenantStamp: null, ct);

            await db.SaveChangesAsync(ct2);
            return 0;
        }, ct);
    }
}

internal static class FeatureFlagCommandHelpers
{
    public static async Task<FeatureFlag> GetLiveFlagAsync(SettingsDbContext db, string key, CancellationToken ct)
    {
        var normalized = FeatureFlag.NormalizeKey(key);
        return await db.FeatureFlags.SingleOrDefaultAsync(f => f.Key == normalized && !f.IsDeleted, ct)
            ?? throw new KeyNotFoundException($"Feature flag '{normalized}' not found.");
    }

    public static TenantFlagOverrideDto ToDto(TenantFeatureFlagOverride o) => new(
        o.Id, o.TenantId, o.FeatureFlagId, o.IsEnabled, o.Reason,
        o.EffectiveFrom, o.EffectiveTo, o.CreatedAt, o.UpdatedAt);
}