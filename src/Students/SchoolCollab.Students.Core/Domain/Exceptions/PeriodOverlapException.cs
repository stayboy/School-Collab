namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a <see cref="Period"/> would overlap another period's
/// <c>[StartDate, EndDate]</c> range, or when activating a period while another
/// period is already <see cref="PeriodStatus.Active"/>. Enforces the
/// "at most one active/current period per year" domain invariant
/// (see documents/specs/grade-level-setup.md §5.6). The overlap check is
/// performed in the command handler (which can query the repository), not in
/// the <see cref="Period"/> entity itself.
/// </summary>
public sealed class PeriodOverlapException : Exception
{
    /// <summary>The id of the period whose activation/update was rejected, if known.</summary>
    public Guid? PeriodId { get; }

    public PeriodOverlapException(string message) : base(message) { }

    public PeriodOverlapException(Guid periodId, string message) : base(message)
        => PeriodId = periodId;
}