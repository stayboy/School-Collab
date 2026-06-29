using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UnpublishAssignmentCommand;

public sealed record UnpublishAssignmentCommand(Guid Id) : ICommand;
