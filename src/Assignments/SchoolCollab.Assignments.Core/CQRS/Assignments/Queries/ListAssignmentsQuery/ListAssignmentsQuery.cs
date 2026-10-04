using SchoolCollab.Core.CQRS;

using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;

/// <summary>
/// The assignment list read. <paramref name="Scope"/> is the caller's resolved
/// <see cref="TeacherScope"/> (round <c>teacher-scope-auth</c> D3) — resolved at the endpoint
/// and passed here; <c>null</c> means "unrestricted", the shape pre-existing callers use and the
/// posture for a principal with no recognised role.
/// </summary>
public sealed record ListAssignmentsQuery(AssignmentStatus? Status, TeacherScope? Scope = null) : IQuery<AssignmentSummaryDto[]>;
