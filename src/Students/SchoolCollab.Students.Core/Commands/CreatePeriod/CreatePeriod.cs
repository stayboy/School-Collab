using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.CreatePeriod;

public sealed record CreatePeriod(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    bool AllowSubjectOverrides = false) : ICommand;