using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.FeatureFlags.Commands;

public sealed record CreateFeatureFlag(
    string Key,
    string Name,
    string? Description,
    bool IsEnabled,
    string Reason) : ICommand;

public sealed record RenameFeatureFlag(
    string Key,
    string Name,
    string? Description,
    string Reason) : ICommand;

public sealed record SetFeatureFlagEnabled(
    string Key,
    bool IsEnabled,
    string Reason) : ICommand;

public sealed record ArchiveFeatureFlag(string Key, string Reason) : ICommand;

public sealed record UnarchiveFeatureFlag(string Key, string Reason) : ICommand;

public sealed record DeleteFeatureFlag(string Key, string Reason) : ICommand;

public sealed record RecoverFeatureFlag(string Key, string Reason) : ICommand;

public sealed record UpsertTenantFlagOverride(
    string Key,
    Guid TenantId,
    bool? IsEnabled,
    string? Value,
    string Reason,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo) : ICommand;

public sealed record DeleteTenantFlagOverride(
    string Key,
    Guid TenantId,
    string Reason) : ICommand;