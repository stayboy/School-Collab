using Microsoft.EntityFrameworkCore;
using SchoolCollab.Config.Contracts.Events;
using SchoolCollab.Config.Core.Data;
using SchoolCollab.Config.Core.Domain;
using SchoolCollab.Config.Core.DTOs;
using SchoolCollab.Config.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Config.Core.CQRS.FeatureFlags.Commands;

public sealed class CreateFeatureFlagHandler(
    ConfigDbContext db,
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
        await db.SaveChangesAsync(ct);

        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, TenantId: null, nameof(FlagChangeKind.Created), flag.IsEnabled, flag.CreatedAt), ct);

        return flag.Id;
    }
}

public sealed class RenameFeatureFlagHandler(
    ConfigDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<RenameFeatureFlag>
{
    public async Task HandleAsync(RenameFeatureFlag command, CancellationToken ct = default)
    {
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, command.Key, ct);
        flag.Rename(command.Name, command.Description);
        auditor.Record(db, tenantId: null, flag.Id, flag.Key, FlagChangeKind.Renamed,
            previousIsEnabled: flag.IsEnabled, newIsEnabled: flag.IsEnabled, command.Reason);
        await db.SaveChangesAsync(ct);

        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, nameof(FlagChangeKind.Renamed), flag.IsEnabled, flag.UpdatedAt), ct);
    }
}

public sealed class SetFeatureFlagEnabledHandler(
    ConfigDbContext db,
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
        await db.SaveChangesAsync(ct);

        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, kind.ToString(), flag.IsEnabled, flag.UpdatedAt), ct);
    }
}

public sealed class ArchiveFeatureFlagHandler(
    ConfigDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<ArchiveFeatureFlag>
{
    public async Task HandleAsync(ArchiveFeatureFlag command, CancellationToken ct = default)
    {
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, command.Key, ct);
        flag.Archive();
        auditor.Record(db, null, flag.Id, flag.Key, FlagChangeKind.Archived,
            flag.IsEnabled, flag.IsEnabled, command.Reason);
        await db.SaveChangesAsync(ct);
        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, nameof(FlagChangeKind.Archived), flag.IsEnabled, flag.UpdatedAt), ct);
    }
}

public sealed class UnarchiveFeatureFlagHandler(
    ConfigDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<UnarchiveFeatureFlag>
{
    public async Task HandleAsync(UnarchiveFeatureFlag command, CancellationToken ct = default)
    {
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, command.Key, ct);
        flag.Unarchive();
        auditor.Record(db, null, flag.Id, flag.Key, FlagChangeKind.Unarchived,
            flag.IsEnabled, flag.IsEnabled, command.Reason);
        await db.SaveChangesAsync(ct);
        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, nameof(FlagChangeKind.Unarchived), flag.IsEnabled, flag.UpdatedAt), ct);
    }
}

public sealed class DeleteFeatureFlagHandler(
    ConfigDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<DeleteFeatureFlag>
{
    public async Task HandleAsync(DeleteFeatureFlag command, CancellationToken ct = default)
    {
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, command.Key, ct);
        flag.Delete();
        auditor.Record(db, null, flag.Id, flag.Key, FlagChangeKind.Deleted,
            flag.IsEnabled, flag.IsEnabled, command.Reason);
        await db.SaveChangesAsync(ct);
        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, nameof(FlagChangeKind.Deleted), flag.IsEnabled, flag.UpdatedAt), ct);
    }
}

public sealed class RecoverFeatureFlagHandler(
    ConfigDbContext db,
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
        await db.SaveChangesAsync(ct);
        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, null, nameof(FlagChangeKind.Recovered), flag.IsEnabled, flag.UpdatedAt), ct);
    }
}

public sealed class UpsertTenantFlagOverrideHandler(
    ConfigDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<UpsertTenantFlagOverride, TenantFlagOverrideDto>
{
    public async Task<TenantFlagOverrideDto> HandleAsync(UpsertTenantFlagOverride command, CancellationToken ct = default)
    {
        var key = FeatureFlag.NormalizeKey(command.Key);
        var flag = await FeatureFlagCommandHelpers.GetLiveFlagAsync(db, key, ct);

        var existing = await db.TenantFlagOverrides
            .IgnoreQueryFilters(["Tenant"])
            .SingleOrDefaultAsync(o => o.TenantId == command.TenantId && o.FeatureFlagId == flag.Id && !o.IsDeleted, ct);

        FlagChangeKind kind;
        bool? previous = null;

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
        await db.SaveChangesAsync(ct);

        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, command.TenantId, kind.ToString(), command.IsEnabled, existing.UpdatedAt), ct);

        return FeatureFlagCommandHelpers.ToDto(existing);
    }
}

public sealed class DeleteTenantFlagOverrideHandler(
    ConfigDbContext db,
    FeatureFlagAuditor auditor,
    IIntegrationEventPublisher publisher) : ICommandHandler<DeleteTenantFlagOverride>
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
        existing.MarkAsDeleted();
        auditor.Record(db, command.TenantId, flag.Id, flag.Key, FlagChangeKind.OverrideDeleted,
            previousIsEnabled: previous, newIsEnabled: null, command.Reason);
        await db.SaveChangesAsync(ct);

        await publisher.EnqueueAsync(new FeatureFlagChanged(
            flag.Id, flag.Key, command.TenantId, nameof(FlagChangeKind.OverrideDeleted), NewIsEnabled: null, existing.UpdatedAt), ct);
    }
}

internal static class FeatureFlagCommandHelpers
{
    public static async Task<FeatureFlag> GetLiveFlagAsync(ConfigDbContext db, string key, CancellationToken ct)
    {
        var normalized = FeatureFlag.NormalizeKey(key);
        return await db.FeatureFlags.SingleOrDefaultAsync(f => f.Key == normalized && !f.IsDeleted, ct)
            ?? throw new KeyNotFoundException($"Feature flag '{normalized}' not found.");
    }

    public static TenantFlagOverrideDto ToDto(TenantFeatureFlagOverride o) => new(
        o.Id, o.TenantId, o.FeatureFlagId, o.IsEnabled, o.Reason,
        o.EffectiveFrom, o.EffectiveTo, o.CreatedAt, o.UpdatedAt);
}