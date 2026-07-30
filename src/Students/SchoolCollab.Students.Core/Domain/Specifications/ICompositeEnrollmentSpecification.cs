namespace SchoolCollab.Students.Core.Domain.Specifications;

/// <summary>
/// The enrollment-validation gateway: an <see cref="IEnrollmentSpecification"/>
/// that AND-combines the registered <see cref="ILeafEnrollmentSpecification"/> leaf rules and,
/// on failure, exposes which rule failed so the handler can throw the matching
/// typed exception. The handler depends on this abstraction (not on the concrete
/// composite or the leaf rules), keeping the validation pipeline swappable as a
/// unit while still allowing rule-to-exception mapping.
/// </summary>
public interface ICompositeEnrollmentSpecification : IEnrollmentSpecification
{
    /// <summary>The first leaf rule that failed during the last
    /// <see cref="IEnrollmentSpecification.IsSatisfiedBy"/> evaluation, or
    /// <c>null</c> when all rules passed.</summary>
    ILeafEnrollmentSpecification? FailingSpecification { get; }
}