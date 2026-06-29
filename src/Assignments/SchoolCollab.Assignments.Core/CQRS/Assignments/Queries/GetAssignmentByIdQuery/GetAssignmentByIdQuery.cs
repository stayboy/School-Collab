using SchoolCollab.Core.CQRS;

using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;

public sealed record GetAssignmentByIdQuery(Guid Id) : IQuery<AssignmentSummaryDto?>;
