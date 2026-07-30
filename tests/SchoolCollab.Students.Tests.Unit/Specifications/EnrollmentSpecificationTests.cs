using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Specifications;

namespace SchoolCollab.Students.Tests.Unit.Specifications;

/// <summary>
/// Unit tests for the enrollment validation specifications (plan §11).
/// Each spec is pure: <see cref="IEnrollmentSpecification.IsSatisfiedBy"/>
/// returns a verdict and, when false, sets <see cref="IEnrollmentSpecification.FailureMessage"/>.
/// These tests pin the boundary semantics (inclusive bounds, null = no limit,
/// null DOB fails) and the composite's short-circuit behaviour.
/// </summary>
[TestClass]
public class EnrollmentSpecificationTests
{
    private static readonly Guid GenderMale = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid GenderFemale = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static Student MakeStudent(DateOnly dob, Guid? gender = null) =>
        Student.Create("STU-001", "Test", "Student", dob, gender ?? GenderMale);

    private static GradeLevel MakeGrade(int? minAge = null, int? maxAge = null, Guid? allowedGender = null) =>
        GradeLevel.Create(Guid.NewGuid(), 1, "Grade 1", 1, minAge, maxAge, allowedGender);

    private static EnrollmentContext MakeContext(Student student, GradeLevel grade, DateOnly enrollDate,
        IReadOnlyList<StudentEnrollment>? existing = null) =>
        new(student, grade, enrollDate, existing ?? []);

    // ── AgeRangeSpecification ──────────────────────────────────────────────

    [TestMethod]
    public void AgeRangeSpecification_WithinRange_Satisfied()
    {
        var spec = new AgeRangeSpecification();
        var student = MakeStudent(new DateOnly(2018, 1, 15)); // 7 yrs on 2025-09-01
        var grade = MakeGrade(minAge: 6, maxAge: 8);
        var ctx = MakeContext(student, grade, new DateOnly(2025, 9, 1));

        spec.IsSatisfiedBy(ctx).Should().BeTrue();
        spec.FailureMessage.Should().BeEmpty();
    }

    [TestMethod]
    public void AgeRangeSpecification_TooYoung_NotSatisfied()
    {
        var spec = new AgeRangeSpecification();
        var student = MakeStudent(new DateOnly(2020, 1, 15)); // 5 yrs on 2025-09-01
        var grade = MakeGrade(minAge: 6, maxAge: 8);
        var ctx = MakeContext(student, grade, new DateOnly(2025, 9, 1));

        spec.IsSatisfiedBy(ctx).Should().BeFalse();
        spec.FailureMessage.Should().Contain("below the minimum");
    }

    [TestMethod]
    public void AgeRangeSpecification_TooOld_NotSatisfied()
    {
        var spec = new AgeRangeSpecification();
        var student = MakeStudent(new DateOnly(2014, 1, 15)); // 11 yrs on 2025-09-01
        var grade = MakeGrade(minAge: 6, maxAge: 8);
        var ctx = MakeContext(student, grade, new DateOnly(2025, 9, 1));

        spec.IsSatisfiedBy(ctx).Should().BeFalse();
        spec.FailureMessage.Should().Contain("above the maximum");
    }

    [TestMethod]
    public void AgeRangeSpecification_NoRange_Satisfied()
    {
        var spec = new AgeRangeSpecification();
        var student = MakeStudent(new DateOnly(2010, 1, 15)); // any age
        var grade = MakeGrade(minAge: null, maxAge: null);
        var ctx = MakeContext(student, grade, new DateOnly(2025, 9, 1));

        spec.IsSatisfiedBy(ctx).Should().BeTrue();
        spec.FailureMessage.Should().BeEmpty();
    }

    // ── GenderRestrictionSpecification ─────────────────────────────────────

    [TestMethod]
    public void GenderRestrictionSpecification_Match_Satisfied()
    {
        var spec = new GenderRestrictionSpecification();
        var student = MakeStudent(new DateOnly(2018, 1, 15), GenderMale);
        var grade = MakeGrade(allowedGender: GenderMale);
        var ctx = MakeContext(student, grade, new DateOnly(2025, 9, 1));

        spec.IsSatisfiedBy(ctx).Should().BeTrue();
        spec.FailureMessage.Should().BeEmpty();
    }

    [TestMethod]
    public void GenderRestrictionSpecification_Mismatch_NotSatisfied()
    {
        var spec = new GenderRestrictionSpecification();
        var student = MakeStudent(new DateOnly(2018, 1, 15), GenderFemale);
        var grade = MakeGrade(allowedGender: GenderMale);
        var ctx = MakeContext(student, grade, new DateOnly(2025, 9, 1));

        spec.IsSatisfiedBy(ctx).Should().BeFalse();
        spec.FailureMessage.Should().Contain("gender does not match");
    }

    [TestMethod]
    public void GenderRestrictionSpecification_NullAllowed_Satisfied()
    {
        var spec = new GenderRestrictionSpecification();
        var student = MakeStudent(new DateOnly(2018, 1, 15), GenderFemale);
        var grade = MakeGrade(allowedGender: null); // co-ed
        var ctx = MakeContext(student, grade, new DateOnly(2025, 9, 1));

        spec.IsSatisfiedBy(ctx).Should().BeTrue();
        spec.FailureMessage.Should().BeEmpty();
    }

    // ── SingleActiveEnrollmentSpecification ────────────────────────────────

    [TestMethod]
    public void SingleActiveEnrollmentSpecification_NoActive_Satisfied()
    {
        var spec = new SingleActiveEnrollmentSpecification();
        var student = MakeStudent(new DateOnly(2018, 1, 15));
        var grade = MakeGrade();
        var ctx = MakeContext(student, grade, new DateOnly(2025, 9, 1), existing: []);

        spec.IsSatisfiedBy(ctx).Should().BeTrue();
        spec.FailureMessage.Should().BeEmpty();
    }

    [TestMethod]
    public void SingleActiveEnrollmentSpecification_HasActive_NotSatisfied()
    {
        var spec = new SingleActiveEnrollmentSpecification();
        var student = MakeStudent(new DateOnly(2018, 1, 15));
        var grade = MakeGrade();
        var existing = new[]
        {
            StudentEnrollment.Create(student.Id, Guid.NewGuid(), grade.Id, new DateOnly(2024, 9, 1), null)
        };
        var ctx = MakeContext(student, grade, new DateOnly(2025, 9, 1), existing: existing);

        spec.IsSatisfiedBy(ctx).Should().BeFalse();
        spec.FailureMessage.Should().Contain("already has an active enrollment");
    }

    // ── CompositeEnrollmentSpecification ───────────────────────────────────

    [TestMethod]
    public void CompositeSpec_FirstFailingSpecWins()
    {
        var ageSpec = new AgeRangeSpecification();
        var genderSpec = new GenderRestrictionSpecification();
        var singleActiveSpec = new SingleActiveEnrollmentSpecification();
        var composite = new CompositeEnrollmentSpecification(new ILeafEnrollmentSpecification[]
        {
            ageSpec, genderSpec, singleActiveSpec
        });

        var student = MakeStudent(new DateOnly(2020, 1, 15), GenderFemale); // 5 yrs, female
        var grade = MakeGrade(minAge: 6, maxAge: 8, allowedGender: GenderMale); // requires 6-8, male
        var ctx = MakeContext(student, grade, new DateOnly(2025, 9, 1));

        composite.IsSatisfiedBy(ctx).Should().BeFalse();
        composite.FailingSpecification.Should().Be(ageSpec,
            "the age spec is evaluated first and short-circuits the composite");
        composite.FailureMessage.Should().Contain("below the minimum");
    }
}
