using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.CompletePeriod;

public sealed record CompletePeriod(Guid Id) : ICommand;