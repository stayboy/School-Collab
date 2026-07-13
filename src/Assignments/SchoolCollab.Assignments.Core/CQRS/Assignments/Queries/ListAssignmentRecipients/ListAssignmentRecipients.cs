using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentRecipients;

/// <summary>List the per-contact publish recipients of an assignment (spec §8/§12).</summary>
public sealed record ListAssignmentRecipients(Guid AssignmentId) : IQuery<AssignmentRecipientDto[]>;