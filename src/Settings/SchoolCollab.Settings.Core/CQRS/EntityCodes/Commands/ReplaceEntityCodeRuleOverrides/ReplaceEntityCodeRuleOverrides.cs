using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.ReplaceEntityCodeRuleOverrides;

/// <summary>
/// Replaces the current tenant's full override set on the rule with id
/// <see cref="GenerationRuleId"/>. The admin UI sends the complete ordered
/// list (not a delta) so the operation is atomic — the rule's effective
/// format is always exactly what the admin sees (spec §4.12).
/// </summary>
public sealed record ReplaceEntityCodeRuleOverrides(
    Guid GenerationRuleId,
    IReadOnlyList<EntityCodeRuleOverrideInput> Overrides) : ICommand;

/// <summary>
/// One row in the override set posted to
/// <see cref="ReplaceEntityCodeRuleOverrides"/>.
/// </summary>
/// <param name="Id">Existing row id; <c>Guid.Empty</c> for a new row.</param>
/// <param name="EntityCodeSegmentId">The segment being overridden.</param>
/// <param name="Field">The <see cref="Domain.OverrideField"/> value (int).</param>
/// <param name="Value">The new value (stringly-typed).</param>
public sealed record EntityCodeRuleOverrideInput(
    Guid Id,
    Guid EntityCodeSegmentId,
    int Field,
    string Value);
