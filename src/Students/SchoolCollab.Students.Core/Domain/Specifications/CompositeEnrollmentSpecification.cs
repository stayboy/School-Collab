using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Domain.Specifications;

/// <summary>
/// AND-combines the registered <see cref="ILeafEnrollmentSpecification"/> leaf rules. Short-
/// circuits on the first failing rule and surfaces that rule's
/// <see cref="IEnrollmentSpecification.FailureMessage"/> plus the failing rule
/// instance (via <see cref="FailingSpecification"/>). Registered in DI as
/// <see cref="ICompositeEnrollmentSpecification"/> — the single
/// enrollment-validation gateway the handler depends on.
/// </summary>
public sealed class CompositeEnrollmentSpecification : ICompositeEnrollmentSpecification
{
    private readonly ILeafEnrollmentSpecification[] _specifications;
    private ILeafEnrollmentSpecification? _failingSpecification;

    public CompositeEnrollmentSpecification(IEnumerable<ILeafEnrollmentSpecification> specifications)
        => _specifications = specifications.ToArray();

    public string FailureMessage { get; private set; } = string.Empty;

    /// <summary>The first rule that failed during the last
    /// <see cref="IsSatisfiedBy"/> evaluation, or <c>null</c> when all passed.</summary>
    public ILeafEnrollmentSpecification? FailingSpecification => _failingSpecification;

    public bool IsSatisfiedBy(EnrollmentContext context)
    {
        foreach (var spec in _specifications)
        {
            if (!spec.IsSatisfiedBy(context))
            {
                _failingSpecification = spec;
                FailureMessage = spec.FailureMessage;
                return false;
            }
        }

        _failingSpecification = null;
        FailureMessage = string.Empty;
        return true;
    }
}