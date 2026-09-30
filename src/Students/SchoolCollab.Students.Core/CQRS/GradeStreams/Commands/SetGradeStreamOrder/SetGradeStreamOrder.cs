using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.SetGradeStreamOrder;

/// <summary>
/// Moves one of a grade's streams to position <paramref name="Order"/> in that
/// grade's stream list (the bridge row's <c>DisplayOrder</c> — the ordering
/// authority; the coded value's own order belongs to the cross-grade GRSTREAMS
/// catalogue). Swap semantics: the stream already at that position takes the
/// mover's old position, and no other row moves. The handler normalises the
/// grade's list to unique contiguous orders first, so a backfill duplicate cannot
/// derail the swap.
/// </summary>
public sealed record SetGradeStreamOrder(
    Guid GradeLevelId,
    Guid AssignmentId,
    int Order) : ICommand;
