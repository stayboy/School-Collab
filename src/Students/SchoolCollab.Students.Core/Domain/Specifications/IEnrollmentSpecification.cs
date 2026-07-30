namespace SchoolCollab.Students.Core.Domain.Specifications;

/// <summary>
/// A single, composable enrollment validation rule. Implementations are pure:
/// <see cref="IsSatisfiedBy"/> returns the verdict and, when it returns
/// <c>false</c>, sets <see cref="FailureMessage"/> to a human/actionable reason.
/// The handler maps the failing rule to a typed domain exception, keeping
/// exception construction in the handler so specs stay free of side effects
/// apart from the message.
/// </summary>
public interface IEnrollmentSpecification
{
    bool IsSatisfiedBy(EnrollmentContext context);
    string FailureMessage { get; }
}