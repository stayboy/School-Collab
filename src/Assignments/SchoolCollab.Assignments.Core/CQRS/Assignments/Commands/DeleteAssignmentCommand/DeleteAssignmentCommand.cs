using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DeleteAssignmentCommand;

public sealed record DeleteAssignmentCommand(Guid Id) : ICommand;
