using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Domain.Specifications;

/// <summary>
/// Satisfied when the grade level has no gender restriction
/// (<see cref="GradeLevel.AllowedGenderCodedValueId"/> is null ⇒ co-ed / no
/// restriction) or the student's <see cref="Student.GenderCodedValueId"/>
/// matches the allowed value.
/// </summary>
public sealed class GenderRestrictionSpecification : ILeafEnrollmentSpecification
{
    public string FailureMessage { get; private set; } = string.Empty;

    public bool IsSatisfiedBy(EnrollmentContext context)
    {
        var allowed = context.GradeLevel.AllowedGenderCodedValueId;
        if (allowed is null)
            return true;

        if (context.Student.GenderCodedValueId != allowed)
        {
            FailureMessage =
                $"Student (ID: {context.Student.Id}) gender does not match the allowed " +
                $"gender for grade '{context.GradeLevel.Name}'.";
            return false;
        }

        return true;
    }
}