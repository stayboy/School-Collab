using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Periods.Commands.DeactivatePeriod;

/// <summary>
/// Deactivates an Active period (period-edit-parity-deactivate.md FR-X1/X7).
/// Only Active periods can be deactivated; deactivating frees the period's date
/// range from the no-overlap check (FR-X3) so a corrected new period can be
/// created. Deactivated periods are not deletable (Draft-only delete).
/// </summary>
public sealed record DeactivatePeriod(Guid Id) : ICommand;
