using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.UpdatePeriod;

public sealed record UpdatePeriod(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    bool AllowSubjectOverrides) : ICommand;