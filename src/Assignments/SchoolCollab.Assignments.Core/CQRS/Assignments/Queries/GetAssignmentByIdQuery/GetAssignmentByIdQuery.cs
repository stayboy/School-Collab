using SchoolCollab.Core.CQRS;

using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;

/// <summary>
/// The id-addressed assignment read. <paramref name="Scope"/> is the caller's resolved
/// <see cref="TeacherScope"/> (round <c>teacher-scope-auth</c> [P2-2]) — the read is reachable by
/// id, so it must apply the same visibility rule as the list and answer <c>null</c> (⇒ 404) for
/// an out-of-scope id. <c>null</c> means unrestricted (the pre-existing callers' shape).
/// </summary>
public sealed record GetAssignmentByIdQuery(Guid Id, TeacherScope? Scope = null) : IQuery<AssignmentSummaryDto?>;
