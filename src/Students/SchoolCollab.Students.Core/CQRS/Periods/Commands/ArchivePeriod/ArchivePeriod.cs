using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.ArchivePeriod;

/// <summary>
/// Archives a period (period-hierarchy-terms-semesters.md FR-H4b). For an
/// AcademicYear, the handler first cascade-completes the year's still-Active
/// sub-periods so an Active sub-period is never orphaned behind a non-Active
/// parent. The HTTP route is POST /students/periods/{id}/archive
/// (PeriodRoutes.cs), used by the grid's Archive action on Deactivated rows
/// (period-edit-parity-deactivate.md FR-X5/X9).
/// </summary>
public sealed record ArchivePeriod(Guid Id) : ICommand;
