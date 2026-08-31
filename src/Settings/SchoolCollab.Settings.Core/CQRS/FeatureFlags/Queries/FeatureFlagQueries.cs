using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.FeatureFlags.Queries;

public sealed record ListFeatureFlags(string? Search, bool IncludeArchived) : IQuery<FeatureFlagDto[]>;

public sealed record GetFeatureFlag(string Key) : IQuery<FeatureFlagDto?>;

public sealed record ListAuditEntries(
    string? Key,
    Guid? TenantId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Skip,
    int Take) : IQuery<FlagAuditEntryDto[]>;

public sealed record ResolveFlagsForTenant(Guid? TenantId) : IQuery<ResolvedFlagDto[]>;

public sealed record ListTenantOverrides(string Key) : IQuery<TenantFlagOverrideDto[]>;