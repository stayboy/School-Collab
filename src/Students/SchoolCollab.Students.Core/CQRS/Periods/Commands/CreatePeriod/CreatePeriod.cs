using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;

/// <summary>
/// A sub-period (Term/Semester) definition supplied alongside a top-level
/// academic year in a single atomic create (FR-C1). Only valid when the year is
/// top-level (<c>ParentPeriodId == null</c>) with a <c>Terms</c>/<c>Semesters</c>
/// division.
/// </summary>
public sealed record SubPeriodDefinition(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>
/// The result of an atomic create: the created top-level academic year id plus
/// the ids of any sub-periods created in the same unit of work (FR-C4).
/// </summary>
public sealed record CreatePeriodResult(
    Guid YearId,
    IReadOnlyList<Guid> SubPeriodIds);

public sealed record CreatePeriod(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    AcademicYearDivision Division,
    Guid? ParentPeriodId = null,
    IReadOnlyList<SubPeriodDefinition>? SubPeriods = null) : ICommand;
