namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// A stream offered by a grade level — the bridge row (<c>AssignmentId</c>)
/// projected together with its resolved coded-value metadata. The metadata comes
/// from the override-resolving Settings <c>by-parent</c> read, so
/// <see cref="Name"/> is the tenant-resolved name and
/// <see cref="NameOverride"/>/<see cref="IsOverridden"/> describe the override
/// itself (null / false for a global blueprint row).
/// </summary>
public sealed record GradeStreamDto(
    Guid AssignmentId,
    Guid StreamCodedValueId,
    Guid GradeLevelId,
    string Code,
    string Name,
    string? NameOverride,
    string? Description,
    string? StreamVersion,
    bool IsOverridden,
    bool IsDisabled,
    int DisplayOrder);
