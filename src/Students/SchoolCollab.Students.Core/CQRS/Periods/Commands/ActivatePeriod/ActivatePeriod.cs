using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;

public sealed record ActivatePeriod(Guid Id) : ICommand;