using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.ArchivePeriod;

/// <summary>
/// Archives a period (period-hierarchy-terms-semesters.md FR-H4b). For an
/// AcademicYear, the handler first cascade-completes the year's still-Active
/// sub-periods so an Active sub-period is never orphaned behind a non-Active
/// parent. No HTTP route exists yet (Rev. 3 keeps the Period API surface
/// unchanged); when an archive endpoint lands it must call this handler.
/// </summary>
public sealed record ArchivePeriod(Guid Id) : ICommand;
