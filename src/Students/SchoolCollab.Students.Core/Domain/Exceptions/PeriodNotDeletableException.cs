namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a <see cref="Period"/> that is NOT in <see cref="PeriodStatus.Draft"/>
/// is deleted, or when deleting a top-level academic year whose sub-periods are not
/// all Draft (period-draft-delete.md FR-D2/FR-D3). Non-Draft periods are referenced
/// by operational data (memberships, assignments, audit entries) and follow
/// Complete -> Archive instead. The API maps this to <c>422 Unprocessable Entity</c>
/// (FR-D8).
/// </summary>
public sealed class PeriodNotDeletableException : Exception
{
    public PeriodNotDeletableException(string message) : base(message) { }
}
