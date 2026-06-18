using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.ActivatePeriod;

public sealed record ActivatePeriod(Guid Id) : ICommand;