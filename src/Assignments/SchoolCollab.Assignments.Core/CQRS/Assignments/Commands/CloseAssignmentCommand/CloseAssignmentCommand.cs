using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CloseAssignmentCommand;

public sealed record CloseAssignmentCommand(Guid Id) : ICommand;
