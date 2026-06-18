using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Core.CQRS;

public sealed record ListAssignmentsQuery(AssignmentStatus? Status) : IQuery<AssignmentSummaryDto[]>;

public sealed record GetAssignmentByIdQuery(Guid Id) : IQuery<AssignmentSummaryDto?>;