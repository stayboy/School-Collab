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
    DateOnly EndDate,
    int? ActivationToleranceDays = null);

/// <summary>
/// The result of an atomic create: the created top-level academic year id plus
/// the ids of any sub-periods created in the same unit of work (FR-C4).
/// </summary>
public sealed record CreatePeriodResult(
    Guid YearId,
    IReadOnlyList<Guid> SubPeriodIds);

/// <summary>
/// Creates a period — a top-level academic year, or (with <see cref="ParentPeriodId"/>
/// set) a single sub-period.
///
/// <para><see cref="Sequence"/> is the 1-based position of a SUB-PERIOD in its year's run
/// of <see cref="Division"/> (v5 §0 decision 15). It is optional and trailing, so every
/// existing positional construction still binds, and it is only legal on the sub-period
/// path: a top-level academic year has no position, which the entity rejects with an
/// <see cref="System.ArgumentException"/> (a 400 at the route). The atomic-create path
/// (<see cref="SubPeriods"/>) deliberately does NOT take positions — its definitions create
/// a whole run at once, and handing them all the same sequence would violate the filtered
/// unique index on <c>(tenant_id, parent_period_id, division, sequence)</c>.</para>
/// </summary>
public sealed record CreatePeriod(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    AcademicYearDivision Division,
    Guid? ParentPeriodId = null,
    IReadOnlyList<SubPeriodDefinition>? SubPeriods = null,
    int? ActivationToleranceDays = null,
    int? Sequence = null) : ICommand;
