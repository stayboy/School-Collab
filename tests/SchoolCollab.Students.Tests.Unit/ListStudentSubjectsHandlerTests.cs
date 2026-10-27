using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentSubjects;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// The §6 "my subjects" union (subject-enrollment-requirement-flag.md FR-ER-16).
/// <para>Acceptance criterion 6: a <c>false</c> subject appears for every student of the
/// grade; a <c>true</c> subject appears only for the students holding a
/// <c>StudentTopicAssignment</c> for the <b>active academic year</b> — a row for a different
/// year does not count (FR-ER-3, the rollover regression).</para>
/// <para>Acceptance criterion 15: a student enrolled in another tenant is invisible, so the
/// union is empty for them.</para>
/// </summary>
[TestClass]
public class ListStudentSubjectsHandlerTests
{
    private static readonly Guid ActiveYearId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PriorYearId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid GradeLevelId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed class StubActivePeriodProvider(ActivePeriod? activeYear) : IActivePeriodProvider
    {
        public Task<ActivePeriod?> GetActivePeriodAsync(CancellationToken ct = default) => Task.FromResult(activeYear);
        public Task<ActivePeriod?> GetCurrentPeriodAsync(CancellationToken ct = default) => Task.FromResult(activeYear);
        public Task<ActivePeriod?> GetActiveAcademicYearAsync(CancellationToken ct = default) => Task.FromResult(activeYear);
        public Task<ActivePeriod?> GetActiveSubPeriodAsync(CancellationToken ct = default) => Task.FromResult(activeYear);
    }

    private static ActivePeriod ActiveYear() => new(
        ActiveYearId, "2026/2027", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31),
        "Active", "AcademicYear", null);

    private static ListStudentSubjectsHandler NewHandler(StudentsTestScope s, ActivePeriod? activeYear) =>
        new(s.Db, new StubActivePeriodProvider(activeYear), new StudentEnrollmentRepository(s.Db));

    private static Guid SeedStudentInGrade(StudentsTestScope s, string number, string firstName, Guid? tenant = null)
    {
        var student = Student.Create(number, firstName, "Pupil", new DateOnly(2015, 1, 1), Guid.NewGuid());
        if (tenant is { } t) student = student.WithTenant(t);
        s.Db.Students.Add(student);

        var enrollment = StudentEnrollment.Create(student.Id, ActiveYearId, GradeLevelId);
        if (tenant is { } other) ((ITenantEntity)enrollment).TenantId = other;
        s.Db.StudentEnrollments.Add(enrollment);

        using (s.TenantAccessor.SuppressTenantGuard())
        {
            s.Db.SaveChanges();
        }

        return student.Id;
    }

    private sealed record GradeSubjects(Guid MathTopicId, Guid ArtTopicId, Guid MathAssignmentId);

    /// <summary>One implied-enrolment (false) subject and one explicit-enrolment (true) subject on the grade.</summary>
    private static async Task<GradeSubjects> SeedGradeSubjectsAsync(StudentsTestScope s)
    {
        var math = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        var art = Topic.Create(Guid.NewGuid(), "ART", "Art", 2);
        s.Db.Topics.AddRange(math, art);

        var mathAssignment = GradeTopicAssignment.Create(GradeLevelId, math.Id, Today());
        var artAssignment = GradeTopicAssignment.Create(GradeLevelId, art.Id, Today());
        artAssignment.SetRequiresEnrollment(true);
        s.Db.GradeTopicAssignments.AddRange(mathAssignment, artAssignment);

        await s.Db.SaveChangesAsync();

        return new GradeSubjects(math.Id, art.Id, mathAssignment.Id);
    }

    private static async Task SeedEnrollmentRowAsync(StudentsTestScope s, Guid studentId, Guid topicId, Guid periodId)
    {
        s.Db.StudentTopicAssignments.Add(StudentTopicAssignment.Create(
            studentId, topicId, periodId, isOverride: false, SubjectAssignmentSource.GradeAssignment));
        await s.Db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task Union_ReturnsTheImpliedSubjectAlways_AndTheExplicitOneOnlyWithRow()
    {
        using var s = new StudentsTestScope("list-student-subjects-union");
        var subjects = await SeedGradeSubjectsAsync(s);
        var enrolledStudentId = SeedStudentInGrade(s, "S1", "Ada");
        var unenrolledStudentId = SeedStudentInGrade(s, "S2", "Kojo");
        await SeedEnrollmentRowAsync(s, enrolledStudentId, subjects.ArtTopicId, ActiveYearId);

        var enrolled = await NewHandler(s, ActiveYear()).HandleAsync(new ListStudentSubjects(enrolledStudentId));
        var unenrolled = await NewHandler(s, ActiveYear()).HandleAsync(new ListStudentSubjects(unenrolledStudentId));

        enrolled.Select(x => x.TopicId).Should().BeEquivalentTo([subjects.MathTopicId, subjects.ArtTopicId],
            "an implied subject is everyone's and the explicit one is held by row");

        var math = enrolled.Single(x => x.TopicId == subjects.MathTopicId);
        math.IsEnrolled.Should().BeTrue("membership is the enrolment for a false subject (FR-ER-1)");
        math.RequiresEnrollment.Should().BeFalse();
        enrolled.Single(x => x.TopicId == subjects.ArtTopicId).IsEnrolled.Should().BeTrue();

        unenrolled.Select(x => x.TopicId).Should().BeEquivalentTo([subjects.MathTopicId],
            "without a row the explicit-enrolment subject is not in the union at all");
    }

    [TestMethod]
    public async Task Union_CarriesTheTopicNameCodeAndBridgeAssignmentId()
    {
        using var s = new StudentsTestScope("list-student-subjects-shape");
        var subjects = await SeedGradeSubjectsAsync(s);
        var studentId = SeedStudentInGrade(s, "S1", "Ada");

        var result = await NewHandler(s, ActiveYear()).HandleAsync(new ListStudentSubjects(studentId));

        var math = result.Single(x => x.TopicId == subjects.MathTopicId);
        math.Name.Should().Be("Mathematics");
        math.Code.Should().Be("MATH");
        math.AssignmentId.Should().Be(subjects.MathAssignmentId,
            "the row must be actionable from the DTO, so it carries the bridge row id");
    }

    [TestMethod]
    public async Task Union_ExcludesARowIssuedForAPriorYear()
    {
        using var s = new StudentsTestScope("list-student-subjects-prior-year");
        var subjects = await SeedGradeSubjectsAsync(s);
        var studentId = SeedStudentInGrade(s, "S1", "Ada");
        // FR-ER-3: eligibility is an exact active-year match, so last year's row does not carry.
        await SeedEnrollmentRowAsync(s, studentId, subjects.ArtTopicId, PriorYearId);

        var result = await NewHandler(s, ActiveYear()).HandleAsync(new ListStudentSubjects(studentId));

        result.Select(x => x.TopicId).Should().BeEquivalentTo([subjects.MathTopicId]);
    }

    [TestMethod]
    public async Task Union_SkipsASubjectWhoseBridgeRowIsNotEffectiveToday()
    {
        using var s = new StudentsTestScope("list-student-subjects-ended");
        var subjects = await SeedGradeSubjectsAsync(s);
        var studentId = SeedStudentInGrade(s, "S1", "Ada");

        var ended = GradeTopicAssignment.Create(
            GradeLevelId, subjects.MathTopicId, Today().AddDays(-30), Today().AddDays(-1));
        // Remove the effective row and replace it with an ended one: the subject is no longer
        // on the grade's list, so it cannot be on the student's either.
        s.Db.GradeTopicAssignments.RemoveRange(
            s.Db.GradeTopicAssignments.Where(x => x.TopicId == subjects.MathTopicId));
        s.Db.GradeTopicAssignments.Add(ended);
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s, ActiveYear()).HandleAsync(new ListStudentSubjects(studentId));

        result.Select(x => x.TopicId).Should().NotContain(subjects.MathTopicId);
    }

    [TestMethod]
    public async Task NoActiveAcademicYear_ReturnsEmpty()
    {
        using var s = new StudentsTestScope("list-student-subjects-no-year");
        await SeedGradeSubjectsAsync(s);
        var studentId = SeedStudentInGrade(s, "S1", "Ada");

        var result = await NewHandler(s, activeYear: null).HandleAsync(new ListStudentSubjects(studentId));

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StudentWithoutActiveEnrolment_ReturnsEmpty()
    {
        using var s = new StudentsTestScope("list-student-subjects-no-enrolment");
        await SeedGradeSubjectsAsync(s);

        var result = await NewHandler(s, ActiveYear()).HandleAsync(new ListStudentSubjects(Guid.NewGuid()));

        result.Should().BeEmpty("the grade is derived from the student's active enrolment — none ⇒ no union");
    }

    [TestMethod]
    public async Task Union_ReturnsNothing_ForAStudentOfAnotherTenant()
    {
        using var s = new StudentsTestScope("list-student-subjects-tenant");
        await SeedGradeSubjectsAsync(s);
        var otherTenantStudentId = SeedStudentInGrade(s, "S9", "Other", tenant: Guid.NewGuid());

        var result = await NewHandler(s, ActiveYear()).HandleAsync(new ListStudentSubjects(otherTenantStudentId));

        result.Should().BeEmpty("both the enrolment and the bridge rows are tenant-filtered");
    }
}
