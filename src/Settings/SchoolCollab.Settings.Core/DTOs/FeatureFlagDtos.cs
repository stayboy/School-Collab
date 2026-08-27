namespace SchoolCollab.Settings.Core.DTOs;

public enum FlagKindDto
{
    Boolean = 0,
    String = 1,
}

public sealed record FeatureFlagDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    FlagKindDto Kind,
    string? Value,
    bool IsEnabled,
    bool IsArchived,
    bool IsDeleted,
    int OverrideCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TenantFlagOverrideDto(
    Guid Id,
    Guid TenantId,
    Guid FeatureFlagId,
    bool? IsEnabled,
    string? Value,
    string Reason,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record FlagAuditEntryDto(
    Guid Id,
    Guid? TenantId,
    Guid FeatureFlagId,
    string FeatureFlagKey,
    string ChangeKind,
    bool? PreviousIsEnabled,
    bool? NewIsEnabled,
    string? PreviousValue,
    string? NewValue,
    string? Reason,
    string ActorId,
    string ActorDisplayName,
    DateTimeOffset OccurredAt);

/// <summary>
/// A single resolved flag for a tenant/global context, returned by the resolve
/// endpoint and consumed by <c>ConfigFeatureFlagService</c>.
/// </summary>
public sealed record ResolvedFlagDto(
    string Key,
    bool IsEnabled,
    string Source,
    DateTimeOffset ResolvedAt);

/// <summary>
/// The effective academic-year division for a tenant (period-hierarchy
/// period-hierarchy-terms-semesters.md FR-H6). <c>Value</c> is one of
/// <c>None</c> | <c>Terms</c> | <c>Semesters</c>.
/// </summary>
public sealed record AcademicYearDivisionDto(string Value, string Source);