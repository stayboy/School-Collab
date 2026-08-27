using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;

public sealed record UpdatePeriod(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    PeriodType PeriodType = PeriodType.AcademicYear,
    Guid? ParentPeriodId = null) : ICommand;