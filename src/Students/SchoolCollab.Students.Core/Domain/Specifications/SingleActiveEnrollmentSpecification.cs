using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Domain.Specifications;

/// <summary>
/// Satisfied when the student has no existing active enrollment. "Active" means
/// <see cref="EnrollmentStatus.Active"/>; the check is cross-period (a student
/// cannot hold active enrollments in two periods simultaneously). Existing
/// active enrollments are grandfathered only insofar as the handler runs this
/// rule for *new* enrollments when the feature flag is on.
/// </summary>
public sealed class SingleActiveEnrollmentSpecification : ILeafEnrollmentSpecification
{
    public string FailureMessage { get; private set; } = string.Empty;

    public bool IsSatisfiedBy(EnrollmentContext context)
    {
        if (context.ExistingActiveEnrollments.Count > 0)
        {
            var ids = string.Join(", ", context.ExistingActiveEnrollments.Select(e => e.Id));
            FailureMessage =
                $"Student (ID: {context.Student.Id}) already has an active enrollment ({ids}). " +
                $"Withdraw or transfer the existing enrollment before enrolling again.";
            return false;
        }

        return true;
    }
}