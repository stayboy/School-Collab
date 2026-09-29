namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown at the API boundary when a supplied <see cref="Period.Sequence"/> is not a legal
/// position at all — under 1, or supplied for a top-level academic year
/// (subject-period-exception-model.md v5 §0 decision 15). The API maps it to
/// <c>422 Unprocessable Entity</c>.
///
/// <para><b>Why this type exists.</b> The entity already rejects both shapes, but it does so
/// with <see cref="ArgumentException"/>, which the period routes map to <c>400 Bad Request</c>
/// — while every other period shape rule (<see cref="PeriodOverlapException"/>,
/// <see cref="PeriodContainmentException"/>, <see cref="PeriodFrameworkMismatchException"/>,
/// <see cref="PeriodSequenceTakenException"/>) is a 422. The same round's position rules would
/// then answer in two different status families depending on which half of the rule the caller
/// broke. This is the boundary half: the handler validates first and the entity's own check
/// stays as defence in depth for callers that bypass the API.</para>
/// </summary>
public sealed class PeriodSequenceInvalidException : Exception
{
    public PeriodSequenceInvalidException(string message) : base(message) { }
}
