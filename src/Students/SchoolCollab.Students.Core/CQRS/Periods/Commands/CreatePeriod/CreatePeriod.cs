using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;

public sealed record CreatePeriod(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate) : ICommand;