using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;

/// <summary>Updates a period's mutable fields. <paramref name="ParentPeriodId"/> is
/// retained (may be null), but there is no <c>AcademicYearDivision</c>: Division is
/// immutable at creation (period-edit-parity-deactivate.md FR-E1), so a period can
/// never change its Terms/Semesters/None framework.</summary>
///
/// <remarks><para><paramref name="Sequence"/> is the sub-period position (v5 §0 decision
/// 15), optional and trailing so existing positional constructions keep binding. Like
/// <paramref name="ActivationToleranceDays"/> it is a <b>full replace</b>: null clears the
/// position. Every caller that edits an already-positioned sub-period must therefore send
/// the value it read back, or it silently unpositions the row.</para></remarks>
public sealed record UpdatePeriod(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid? ParentPeriodId = null,
    int? ActivationToleranceDays = null,
    int? Sequence = null) : ICommand;
