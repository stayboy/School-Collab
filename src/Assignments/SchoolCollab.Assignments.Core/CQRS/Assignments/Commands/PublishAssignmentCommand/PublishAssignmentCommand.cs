using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;

public sealed record PublishAssignmentCommand(Guid Id, IReadOnlyList<Guid>? ContactIds = null) : ICommand;
