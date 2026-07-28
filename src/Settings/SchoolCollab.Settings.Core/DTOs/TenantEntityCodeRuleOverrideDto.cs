using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// Wire-format <see cref="TenantEntityCodeRuleOverride"/> row. Used by the
/// <c>GET /api/entity-code-rules/{id}/overrides</c> and
/// <c>PUT /api/entity-code-rules/{id}/overrides</c> endpoints (spec §4.12).
/// <see cref="Field"/> and <see cref="Value"/> map directly to the persisted
/// columns; <see cref="SegmentIndex"/> is included for admin UI convenience
/// (the rule's segments are loaded in the same request, so the client can
/// group by index without an extra round-trip).
/// </summary>
public sealed record TenantEntityCodeRuleOverrideDto(
    Guid Id,
    Guid GenerationRuleId,
    Guid EntityCodeSegmentId,
    int SegmentIndex,
    int Field,
    string Value)
{
    public static TenantEntityCodeRuleOverrideDto FromOverride(
        TenantEntityCodeRuleOverride row,
        int segmentIndex) => new(
        row.Id,
        row.GenerationRuleId,
        row.EntityCodeSegmentId,
        segmentIndex,
        (int)row.Field,
        row.Value);
}
