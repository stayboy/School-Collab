using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Domain.Specifications;

/// <summary>
/// Satisfied when the student's age (whole years between DateOfBirth and the
/// enrollment date, inclusive bounds) is within the grade level's
/// <c>[MinAge, MaxAge]</c>. A null bound means no limit on that side. A null
/// DateOfBirth fails (students require DOB per <see cref="Student.Create"/>).
/// </summary>
public sealed class AgeRangeSpecification : ILeafEnrollmentSpecification
{
    public string FailureMessage { get; private set; } = string.Empty;

    public bool IsSatisfiedBy(EnrollmentContext context)
    {
        var dob = context.Student.DateOfBirth;
        if (dob is null)
        {
            FailureMessage =
                $"Student (ID: {context.Student.Id}) has no date of birth; " +
                $"cannot evaluate age range for grade '{context.GradeLevel.Name}'.";
            return false;
        }

        var age = ComputeAge(dob.Value, context.EnrollmentDate);
        var min = context.GradeLevel.MinAge;
        var max = context.GradeLevel.MaxAge;

        if (min is not null && age < min.Value)
        {
            FailureMessage =
                $"Student (ID: {context.Student.Id}) is {age} years old (DOB {dob.Value:yyyy-MM-dd}), " +
                $"below the minimum of {min.Value} for grade '{context.GradeLevel.Name}'.";
            return false;
        }

        if (max is not null && age > max.Value)
        {
            FailureMessage =
                $"Student (ID: {context.Student.Id}) is {age} years old (DOB {dob.Value:yyyy-MM-dd}), " +
                $"above the maximum of {max.Value} for grade '{context.GradeLevel.Name}'.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whole years between <paramref name="dateOfBirth"/> and
    /// <paramref name="asOfDate"/>, accounting for whether the birthday has
    /// occurred yet in the as-of year (leap-day safe via
    /// <see cref="DateOnly.AddYears"/>).
    /// </summary>
    internal static int ComputeAge(DateOnly dateOfBirth, DateOnly asOfDate)
    {
        var age = asOfDate.Year - dateOfBirth.Year;
        if (asOfDate < dateOfBirth.AddYears(age))
            age--;
        return age;
    }
}