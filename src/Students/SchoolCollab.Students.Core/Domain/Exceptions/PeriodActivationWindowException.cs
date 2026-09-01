namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when activating a period whose <c>[StartDate, EndDate]</c> is far away from
/// today — outside the activation window <c>[StartDate − tol, EndDate + tol]</c>, where
/// tol is the per-period override (<see cref="Domain.Period.ActivationToleranceDays"/>) or
/// the global default (<c>Students:PeriodActivationToleranceDays</c>). The guard is a hard,
/// always-on invariant (period-activation-window-auto-activation.md FR-W1). The API maps
/// this to <c>422 Unprocessable Entity</c> (FR-W6).
/// </summary>
public sealed class PeriodActivationWindowException : Exception
{
    public PeriodActivationWindowException(string message) : base(message) { }
}
