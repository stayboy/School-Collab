using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;

public sealed record CreatePeriod(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    AcademicYearDivision Division,
    Guid? ParentPeriodId = null) : ICommand;
