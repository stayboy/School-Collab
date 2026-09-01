using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;

/// <summary>Updates a period's mutable fields. <paramref name="ParentPeriodId"/> is
/// retained (may be null), but there is no <c>AcademicYearDivision</c>: Division is
/// immutable at creation (period-edit-parity-deactivate.md FR-E1), so a period can
/// never change its Terms/Semesters/None framework.</summary>
public sealed record UpdatePeriod(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid? ParentPeriodId = null) : ICommand;
