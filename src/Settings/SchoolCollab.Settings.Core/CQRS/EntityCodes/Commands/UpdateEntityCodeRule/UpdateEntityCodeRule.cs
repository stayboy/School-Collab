using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.CreateEntityCodeRule;

namespace SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.UpdateEntityCodeRule;

/// <summary>
/// Updates an existing <see cref="Domain.EntityCodeRule"/>: name/description/active
/// and the full ordered segment list (replace-all, spec §4.8). Changing the
/// template restarts the per-segment sequence state.
/// </summary>
public sealed record UpdateEntityCodeRule(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<EntityCodeSegmentInput> Segments) : ICommand;