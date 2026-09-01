namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a <see cref="Period"/> that is NOT in <see cref="PeriodStatus.Active"/>
/// is deactivated (period-edit-parity-deactivate.md FR-X1). Only Active periods can be
/// deactivated, which frees their date range from the no-overlap check so a corrected
/// new period can be created (FR-X3). The API maps this to <c>422 Unprocessable Entity</c>
/// (FR-X7). Mirrors <see cref="PeriodNotDeletableException"/> — Active/Completed/Archived/
/// Deactivated periods are referenced by operational data and follow Complete/Archive.
/// </summary>
public sealed class PeriodNotDeactivatableException : Exception
{
    public PeriodNotDeactivatableException(string message) : base(message) { }
}
