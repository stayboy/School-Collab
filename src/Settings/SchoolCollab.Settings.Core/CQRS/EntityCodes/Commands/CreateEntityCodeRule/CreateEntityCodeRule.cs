using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.CreateEntityCodeRule;

/// <summary>
/// Creates an <see cref="Domain.EntityCodeRule"/> with its initial
/// <see cref="Domain.EntityCodeSegment"/> children (spec §4.8). The admin UI
/// posts the full rule + segment list in a single PUT.
/// </summary>
public sealed record CreateEntityCodeRule(
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<EntityCodeSegmentInput> Segments) : ICommand;

public sealed record EntityCodeSegmentInput(
    int Index,
    string? Role,
    Domain.SegmentType Type,
    string? FixedText,
    string? Prefix,
    string? Suffix,
    Domain.ResetPeriod ResetPeriod,
    int MinWidth,
    string? UpperLimit);