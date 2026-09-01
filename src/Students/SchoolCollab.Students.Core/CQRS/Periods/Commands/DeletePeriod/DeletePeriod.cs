using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.DeletePeriod;

/// <summary>
/// Deletes a <see cref="Domain.Period"/> that is in <see cref="Domain.PeriodStatus.Draft"/>
/// (period-draft-delete.md FR-D1). Deleting a top-level academic year also deletes its
/// Draft sub-periods via the declared EF cascade (FR-D3). Non-Draft periods are rejected
/// (FR-D2).
/// </summary>
public sealed record DeletePeriod(Guid Id) : ICommand;
