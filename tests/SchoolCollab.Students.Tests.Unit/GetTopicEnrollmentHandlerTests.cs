using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.GetTopicEnrollment;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// <c>GET /students/topic-enrollment</c> — the publish filter's eligibility answer
/// (subject-enrollment-requirement-flag.md FR-ER-14, round 13 P4).
/// <para>Acceptance criterion 7: no period parameter (the active academic year is resolved
/// server-side), and both arrays come back — <c>Audience</c> is the grade roster,
/// <c>Eligible</c> the enrolled subset, and for a <c>false</c> subject they are identical.</para>
/// <para>Acceptance criterion 15: a grade, topic or roster belonging to another tenant returns
/// nothing.</para>
/// </summary>
[TestClass]
public class GetTopicEnrollmentHandlerTests
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

    private static GetTopicEnrollmentHandler NewHandler(StudentsTestScope s, ActivePeriod? activeYear) =>
        new(s.Db, new StubActivePeriodProvider(activeYear));

    private static Guid SeedStudentInGrade(
        StudentsTestScope s, string number, Guid gradeLevelId, Guid? tenant = null)
    {
        var student = Student.Create(number, number, "Pupil", new DateOnly(2015, 1, 1), Guid.NewGuid());
        if (tenant is { } t) student = student.WithTenant(t);
        s.Db.Students.Add(student);

        var enrollment = StudentEnrollment.Create(student.Id, ActiveYearId, gradeLevelId);
        if (tenant is { } other) ((ITenantEntity)enrollment).TenantId = other;
        s.Db.StudentEnrollments.Add(enrollment);

        using (s.TenantAccessor.SuppressTenantGuard())
        {
            s.Db.SaveChanges();
        }

        return student.Id;
    }

    private static async Task<GradeTopicAssignment> SeedGradeSubjectAsync(
        StudentsTestScope s, Guid gradeLevelId, bool requiresEnrollment, Guid? tenant = null)
    {
        var topic = Topic.Create(Guid.NewGuid(), "SUBJ", "Subject", 1);
        if (tenant is { } t) ((ITenantEntity)topic).TenantId = t;
        s.Db.Topics.Add(topic);

        var bridge = GradeTopicAssignment.Create(gradeLevelId, topic.Id, Today());
        if (requiresEnrollment) bridge.SetRequiresEnrollment(true);
        if (tenant is { } other) ((ITenantEntity)bridge).TenantId = other;
        s.Db.GradeTopicAssignments.Add(bridge);

        using (s.TenantAccessor.SuppressTenantGuard())
        {
            await s.Db.SaveChangesAsync();
        }

        return bridge;
    }

    private static async Task SeedEnrollmentRowAsync(
        StudentsTestScope s, Guid studentId, Guid topicId, Guid periodId)
    {
        s.Db.StudentTopicAssignments.Add(StudentTopicAssignment.Create(
            studentId, topicId, periodId, isOverride: false, SubjectAssignmentSource.GradeAssignment));
        await s.Db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task FalseSubject_ReturnsTheSameArrayForAudienceAndEligible()
    {
        using var s = new StudentsTestScope("topic-enrollment-false-subject");
        var bridge = await SeedGradeSubjectAsync(s, GradeLevelId, requiresEnrollment: false);
        var first = SeedStudentInGrade(s, "S1", GradeLevelId);
        var second = SeedStudentInGrade(s, "S2", GradeLevelId);

        var result = await NewHandler(s, ActiveYear())
            .HandleAsync(new GetTopicEnrollment(GradeLevelId, bridge.TopicId));

        result.Audience.Should().BeEquivalentTo([first, second]);
        result.Eligible.Should().BeEquivalentTo(result.Audience,
            "a false subject enrols every member of the grade by implication (FR-ER-1) — nothing to exclude");
    }

    [TestMethod]
    public async Task TrueSubject_AudienceIsTheRoster_AndEligibleIsTheEnrolledSubset()
    {
        using var s = new StudentsTestScope("topic-enrollment-true-subject");
        var bridge = await SeedGradeSubjectAsync(s, GradeLevelId, requiresEnrollment: true);
        var enrolledStudentId = SeedStudentInGrade(s, "S1", GradeLevelId);
        var unenrolledStudentId = SeedStudentInGrade(s, "S2", GradeLevelId);
        await SeedEnrollmentRowAsync(s, enrolledStudentId, bridge.TopicId, ActiveYearId);

        var result = await NewHandler(s, ActiveYear())
            .HandleAsync(new GetTopicEnrollment(GradeLevelId, bridge.TopicId));

        result.Audience.Should().BeEquivalentTo([enrolledStudentId, unenrolledStudentId],
            "the audience is the whole grade roster");
        result.Eligible.Should().BeEquivalentTo([enrolledStudentId],
            "only the student holding a row for the active year is enrolled");
    }

    [TestMethod]
    public async Task TrueSubject_IgnoresARowIssuedForAPriorYear()
    {
        using var s = new StudentsTestScope("topic-enrollment-prior-year");
        var bridge = await SeedGradeSubjectAsync(s, GradeLevelId, requiresEnrollment: true);
        var studentId = SeedStudentInGrade(s, "S1", GradeLevelId);
        // The rollover regression (FR-ER-3): eligibility is an exact active-year match.
        await SeedEnrollmentRowAsync(s, studentId, bridge.TopicId, PriorYearId);

        var result = await NewHandler(s, ActiveYear())
            .HandleAsync(new GetTopicEnrollment(GradeLevelId, bridge.TopicId));

        result.Eligible.Should().BeEmpty();
    }

    [TestMethod]
    public async Task NoEffectiveBridgeRow_ReturnsBothArraysEmpty()
    {
        using var s = new StudentsTestScope("topic-enrollment-no-bridge");
        SeedStudentInGrade(s, "S1", GradeLevelId);

        var result = await NewHandler(s, ActiveYear())
            .HandleAsync(new GetTopicEnrollment(GradeLevelId, Guid.NewGuid()));

        result.Audience.Should().BeEmpty("no effective link means no subject to be enrolled in");
        result.Eligible.Should().BeEmpty();
    }

    [TestMethod]
    public async Task NoActiveAcademicYear_KeepsTheAudience_AndEmptiesEligible()
    {
        using var s = new StudentsTestScope("topic-enrollment-no-year");
        var bridge = await SeedGradeSubjectAsync(s, GradeLevelId, requiresEnrollment: true);
        SeedStudentInGrade(s, "S1", GradeLevelId);
        SeedStudentInGrade(s, "S2", GradeLevelId);

        var result = await NewHandler(s, activeYear: null)
            .HandleAsync(new GetTopicEnrollment(GradeLevelId, bridge.TopicId));

        result.Audience.Should().HaveCount(2,
            "the roster is still the grade's, so a true subject excludes the whole grade rather than over-sending");
        result.Eligible.Should().BeEmpty("nothing can be enrolled without an academic year");
    }

    [TestMethod]
    public async Task ReturnsNothing_ForAnotherTenantsGradeAndSubject()
    {
        using var s = new StudentsTestScope("topic-enrollment-tenant");
        var (otherTenantGradeId, otherTenantTopicId) = await SeedOtherTenantGradeSubjectAsync(s);

        var result = await NewHandler(s, ActiveYear())
            .HandleAsync(new GetTopicEnrollment(otherTenantGradeId, otherTenantTopicId));

        result.Audience.Should().BeEmpty("the bridge row and the roster are tenant-filtered");
        result.Eligible.Should().BeEmpty();
    }

    private static async Task<(Guid GradeLevelId, Guid TopicId)> SeedOtherTenantGradeSubjectAsync(
        StudentsTestScope s)
    {
        var otherTenant = Guid.NewGuid();
        var gradeLevelId = Guid.NewGuid();
        var bridge = await SeedGradeSubjectAsync(s, gradeLevelId, requiresEnrollment: false, tenant: otherTenant);
        SeedStudentInGrade(s, "S9", gradeLevelId, tenant: otherTenant);
        return (gradeLevelId, bridge.TopicId);
    }
}
