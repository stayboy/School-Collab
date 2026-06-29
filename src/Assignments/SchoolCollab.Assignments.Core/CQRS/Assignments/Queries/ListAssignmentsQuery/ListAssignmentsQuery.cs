using SchoolCollab.Core.CQRS;

using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;

public sealed record ListAssignmentsQuery(AssignmentStatus? Status) : IQuery<AssignmentSummaryDto[]>;
