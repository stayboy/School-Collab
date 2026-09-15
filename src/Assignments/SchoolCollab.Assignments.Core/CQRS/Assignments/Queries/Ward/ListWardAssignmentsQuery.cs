using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.Ward;

/// <summary>
/// WS-A5 — the list of assignments a ward can see and act on: Published (and
/// Scheduled-active per the existing visibility rule) assignments the student
/// is already a linked recipient of (recipient-link targeting — full
/// grade/group targeting resolution is cross-context and lands in slice 2b).
/// Each row carries due date, submission state, and whether required content
/// modules remain locked.
/// </summary>
public sealed record ListWardAssignments(Guid StudentId) : IQuery<WardAssignmentListItemDto[]>;
