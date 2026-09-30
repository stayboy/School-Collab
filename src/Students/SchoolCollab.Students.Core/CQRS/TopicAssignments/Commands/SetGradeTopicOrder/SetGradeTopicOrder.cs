using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.SetGradeTopicOrder;

/// <summary>
/// Moves one of a grade's subjects to position <paramref name="Order"/> in that
/// grade's curriculum list (the bridge row's <c>DisplayOrder</c> — the ordering
/// authority; the shared <c>Topic.DisplayOrder</c> orders the cross-grade subject
/// catalogue instead). Swap semantics: the assignment already at that position
/// takes the mover's old position, and no other row moves. The handler normalises
/// the grade's list to unique contiguous orders first, so a backfill duplicate
/// cannot derail the swap.
/// </summary>
/// <remarks>
/// The command carries <b>no grade id</b>: the route is id-only, and a grade id
/// supplied alongside an assignment id could lie. The handler derives the grade
/// (and the tenant) scope from the loaded, tenant-filtered bridge row itself, so
/// an unknown or other-tenant assignment id fails the load and maps to 404.
/// </remarks>
public sealed record SetGradeTopicOrder(Guid AssignmentId, int Order) : ICommand;
