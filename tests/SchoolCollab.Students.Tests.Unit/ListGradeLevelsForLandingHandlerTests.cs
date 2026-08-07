using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevelsForLanding;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class ListGradeLevelsForLandingHandlerTests
{
    private static ListGradeLevelsForLandingHandler NewHandler(StudentsTestScope s) =>
        new(s.Db, s.Tenants, s.Cache);

    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s, Guid codedValueId, int level, string name) =>
        await SeedGradeLevelAsync(s, codedValueId, level, name, level);

    private static async Task<Guid> SeedGradeLevelAsync(
        StudentsTestScope s, Guid codedValueId, int level, string name, int displayOrder,
        int? minAge = null, int? maxAge = null, Guid? allowedGenderCodedValueId = null)
    {
        var gl = GradeLevel.Create(codedValueId, level, name, displayOrder,
            minAge, maxAge, allowedGenderCodedValueId);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    private static async Task<Guid> SeedCurrentPeriodAsync(StudentsTestScope s, string name)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var period = Period.Create(name, today.AddDays(-1), today.AddDays(1));
        s.Db.Periods.Add(period);
        await s.Db.SaveChangesAsync();
        return period.Id;
    }

    [TestMethod]
    public async Task Landing_NoTopicsNoStudents_ZeroCounts()
    {
        using var s = new StudentsTestScope("landing-no-period");
        await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        var row = result[0];
        row.TopicCount.Should().Be(0);
        row.StudentCount.Should().Be(0);
        row.StrandCount.Should().Be(0);
        row.LessonCount.Should().Be(0);
    }

    [TestMethod]
    public async Task Landing_WithCurrentPeriod_CountsTopicsAndStudents()
    {
        using var s = new StudentsTestScope("landing-with-period");
        var periodId = await SeedCurrentPeriodAsync(s, "Term 1");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");

        // One topic assigned to this grade, effective from today and open-ended → TopicCount 1.
        var topic = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        s.Db.Topics.Add(topic);
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(glId, topic.Id, DateOnly.FromDateTime(DateTime.UtcNow)));
        await s.Db.SaveChangesAsync();

        // One current-tenant student enrolled in this grade for the current period.
        var student = Student.Create("S1", "Anna", "Smith", new DateOnly(2015, 1, 1), Guid.NewGuid());
        student.WithTenant(s.Tenants); // current tenant (System/Empty)
        s.Db.Students.Add(student);
        s.Db.StudentEnrollments.Add(StudentEnrollment.Create(student.Id, periodId, glId));
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        var row = result[0];
        row.TopicCount.Should().Be(1);
        row.StudentCount.Should().Be(1);
    }

    [TestMethod]
    public async Task Landing_StudentCount_IsTenantScoped()
    {
        using var s = new StudentsTestScope("landing-tenant-scope");
        var periodId = await SeedCurrentPeriodAsync(s, "Term 1");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");

        // Current-tenant student → counted.
        var s1 = Student.Create("S1", "Anna", "Smith", new DateOnly(2015, 1, 1), Guid.NewGuid())
            .WithTenant(s.Tenants);
        s.Db.Students.Add(s1);
        s.Db.StudentEnrollments.Add(StudentEnrollment.Create(s1.Id, periodId, glId));

        // Other-tenant student → NOT counted (tenant query filter excludes them).
        var otherTenant = Guid.NewGuid();
        var s2 = Student.Create("S2", "Bob", "Jones", new DateOnly(2015, 1, 1), Guid.NewGuid())
            .WithTenant(otherTenant);
        s.Db.Students.Add(s2);
        s.Db.StudentEnrollments.Add(StudentEnrollment.Create(s2.Id, periodId, glId));
        // s2 belongs to another tenant — suppress the save-guard (FR-6) for this test setup.
        using (s.TenantAccessor.SuppressTenantGuard())
        {
            await s.Db.SaveChangesAsync();
        }

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        result[0].StudentCount.Should().Be(1, "only the current tenant's student is counted");
    }

    [TestMethod]
    public async Task Landing_WithdrawnEnrollment_NotCounted()
    {
        using var s = new StudentsTestScope("landing-withdrawn");
        var periodId = await SeedCurrentPeriodAsync(s, "Term 1");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");

        var student = Student.Create("S1", "Anna", "Smith", new DateOnly(2015, 1, 1), Guid.NewGuid())
            .WithTenant(s.Tenants);
        s.Db.Students.Add(student);
        var enrollment = StudentEnrollment.Create(student.Id, periodId, glId);
        enrollment.Withdraw(); // Active → Withdrawn
        s.Db.StudentEnrollments.Add(enrollment);
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        result[0].StudentCount.Should().Be(0, "only Active enrollments are counted");
    }

    // Projection regression: PR #94 added MinAge/MaxAge/AllowedGenderCodedValueId
    // to GradeLevelLandingDto but the original handler projection never selected
    // the new columns — the landing page was rendering all-null validation rules
    // (the bug the user reported). These tests pin the projection so the regression
    // cannot return without the handler test failing.
    [TestMethod]
    public async Task Landing_ProjectsValidationRules_AllThreeSet()
    {
        using var s = new StudentsTestScope("landing-validation-projection-all");
        var genderId = Guid.NewGuid();
        await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1", displayOrder: 1,
            minAge: 10, maxAge: 12, allowedGenderCodedValueId: genderId);

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        var row = result[0];
        row.MinAge.Should().Be(10);
        row.MaxAge.Should().Be(12);
        row.AllowedGenderCodedValueId.Should().Be(genderId);
    }

    [TestMethod]
    public async Task Landing_ProjectsEnrollmentBlockedFlag()
    {
        using var s = new StudentsTestScope("landing-blocked-projection");
        var codedValueId = Guid.NewGuid();
        var gl = GradeLevel.Create(codedValueId, 1, "Grade 1", 1, isBlockedFromEnrollment: true);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        result[0].IsBlockedFromEnrollment.Should().BeTrue(
            "the landing DTO must project the enrollment-blocked flag for the landing toggle");
    }

    [TestMethod]
    public async Task Landing_StrandCount_CountsAcrossEffectiveTopics()
    {
        using var s = new StudentsTestScope("landing-strand-count");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");

        // Two topics assigned to this grade, effective from today.
        var topicA = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        var topicB = Topic.Create(Guid.NewGuid(), "SCI", "Science", 2);
        s.Db.Topics.Add(topicA);
        s.Db.Topics.Add(topicB);
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(glId, topicA.Id, DateOnly.FromDateTime(DateTime.UtcNow)));
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(glId, topicB.Id, DateOnly.FromDateTime(DateTime.UtcNow)));

        // 3 strands on topic A, 1 strand on topic B → StrandCount 4.
        s.Db.TopicStrands.Add(TopicStrand.Create(topicA.Id, "Algebra", null, 1));
        s.Db.TopicStrands.Add(TopicStrand.Create(topicA.Id, "Geometry", null, 2));
        s.Db.TopicStrands.Add(TopicStrand.Create(topicA.Id, "Statistics", null, 3));
        s.Db.TopicStrands.Add(TopicStrand.Create(topicB.Id, "Biology", null, 1));
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        result[0].StrandCount.Should().Be(4);
    }

    [TestMethod]
    public async Task Landing_LessonCount_CountsAcrossEffectiveTopics()
    {
        using var s = new StudentsTestScope("landing-lesson-count");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");

        // Two topics assigned to this grade, effective from today.
        var topicA = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        var topicB = Topic.Create(Guid.NewGuid(), "SCI", "Science", 2);
        s.Db.Topics.Add(topicA);
        s.Db.Topics.Add(topicB);
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(glId, topicA.Id, DateOnly.FromDateTime(DateTime.UtcNow)));
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(glId, topicB.Id, DateOnly.FromDateTime(DateTime.UtcNow)));

        // 5 lessons on topic A, 2 lessons on topic B → LessonCount 7.
        // A lesson is a strand with a parent; each needs a root strand to parent it.
        var strandA = TopicStrand.Create(topicA.Id, "Strand A", null, 1);
        var strandB = TopicStrand.Create(topicB.Id, "Strand B", null, 1);
        s.Db.TopicStrands.Add(strandA);
        s.Db.TopicStrands.Add(strandB);
        s.Db.TopicStrands.Add(TopicStrand.Create(topicA.Id, "Lesson 1", null, 1, strandA.Id));
        s.Db.TopicStrands.Add(TopicStrand.Create(topicA.Id, "Lesson 2", null, 2, strandA.Id));
        s.Db.TopicStrands.Add(TopicStrand.Create(topicA.Id, "Lesson 3", null, 3, strandA.Id));
        s.Db.TopicStrands.Add(TopicStrand.Create(topicA.Id, "Lesson 4", null, 4, strandA.Id));
        s.Db.TopicStrands.Add(TopicStrand.Create(topicA.Id, "Lesson 5", null, 5, strandA.Id));
        s.Db.TopicStrands.Add(TopicStrand.Create(topicB.Id, "Lesson A", null, 1, strandB.Id));
        s.Db.TopicStrands.Add(TopicStrand.Create(topicB.Id, "Lesson B", null, 2, strandB.Id));
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        result[0].LessonCount.Should().Be(7);
    }

    [TestMethod]
    public async Task Landing_ArchivedTopic_StrandsAndLessonsExcluded()
    {
        using var s = new StudentsTestScope("landing-archived-topic");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var topic = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        s.Db.Topics.Add(topic);

        // Archived topic: EndDate is yesterday.
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(glId, topic.Id, today.AddDays(-30), today.AddDays(-1)));
        var strand = TopicStrand.Create(topic.Id, "Algebra", null, 1);
        s.Db.TopicStrands.Add(strand);
        s.Db.TopicStrands.Add(TopicStrand.Create(topic.Id, "Lesson 1", null, 1, strand.Id));
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        result[0].TopicCount.Should().Be(0, "archived topic is not effective");
        result[0].StrandCount.Should().Be(0, "strands of archived topic are excluded");
        result[0].LessonCount.Should().Be(0, "lessons of archived topic are excluded");
    }

    [TestMethod]
    public async Task Landing_NoCurrentPeriod_StrandAndLessonCountsStillComputed()
    {
        using var s = new StudentsTestScope("landing-no-period-strands-lessons");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");

        var topic = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        s.Db.Topics.Add(topic);
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(glId, topic.Id, DateOnly.FromDateTime(DateTime.UtcNow)));
        var strand = TopicStrand.Create(topic.Id, "Algebra", null, 1);
        s.Db.TopicStrands.Add(strand);
        s.Db.TopicStrands.Add(TopicStrand.Create(topic.Id, "Lesson 1", null, 1, strand.Id));
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        result[0].StrandCount.Should().Be(1, "strand count is not gated on period");
        result[0].LessonCount.Should().Be(1, "lesson count is not gated on period");
        result[0].StudentCount.Should().Be(0, "no enrollments exist");
    }
}