using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.CompletePeriod;

public sealed record CompletePeriod(Guid Id) : ICommand;