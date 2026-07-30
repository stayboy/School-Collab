namespace SchoolCollab.Students.Core.Domain.Specifications;

/// <summary>
/// Marker for a leaf enrollment-validation rule (age, gender, single-active).
/// Leaf rules implement this so the composite can depend on
/// <c>IEnumerable&lt;<see cref="ILeafEnrollmentSpecification"/>&gt;</c> without pulling in the
/// composite itself (which is registered as
/// <see cref="ICompositeEnrollmentSpecification"/>, not as a rule) — avoiding a
/// circular resolution.
/// </summary>
public interface ILeafEnrollmentSpecification : IEnrollmentSpecification { }