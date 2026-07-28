using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// Read-model for an <see cref="EntityCodeRule"/> aggregate (rule + ordered
/// segments). Used by the admin API and the segment-editor UI (spec §4.11).
/// </summary>
public sealed record EntityCodeRuleDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    Guid? TenantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<EntityCodeSegmentDto> Segments)
{
    public static EntityCodeRuleDto FromRule(EntityCodeRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var segments = rule.Segments
            .OrderBy(s => s.Index)
            .Select(EntityCodeSegmentDto.FromSegment)
            .ToList();
        return new EntityCodeRuleDto(
            rule.Id,
            rule.Code,
            rule.Name,
            rule.Description,
            rule.IsActive,
            rule.TenantId,
            rule.CreatedAt,
            rule.UpdatedAt,
            segments);
    }
}

/// <summary>
/// Read-model for an <see cref="EntityCodeSegment"/> within a rule template.
/// </summary>
public sealed record EntityCodeSegmentDto(
    Guid Id,
    int Index,
    string? Role,
    SegmentType Type,
    string FixedText,
    string Prefix,
    string Suffix,
    ResetPeriod ResetPeriod,
    int MinWidth,
    string? UpperLimit,
    int LastSequence,
    string? LastPrefix,
    string? LastPeriodBucket)
{
    public static EntityCodeSegmentDto FromSegment(EntityCodeSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return new EntityCodeSegmentDto(
            segment.Id,
            segment.Index,
            segment.Role,
            segment.Type,
            segment.FixedText,
            segment.Prefix,
            segment.Suffix,
            segment.ResetPeriod,
            segment.MinWidth,
            segment.UpperLimit,
            segment.LastSequence,
            segment.LastPrefix,
            segment.LastPeriodBucket);
    }
}