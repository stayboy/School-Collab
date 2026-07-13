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
        StudentsTestScope s, Guid codedValueId, int level, string name, int displayOrder)
    {
        var gl = GradeLevel.Create(codedValueId, level, name, displayOrder);
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
    public async Task Landing_NoCurrentPeriod_ZeroCountsAndNullPeriod()
    {
        using var s = new StudentsTestScope("landing-no-period");
        await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        var row = result[0];
        row.SubjectCount.Should().Be(0);
        row.StudentCount.Should().Be(0);
        row.CurrentPeriodId.Should().BeNull();
        row.CurrentPeriodName.Should().BeNull();
    }

    [TestMethod]
    public async Task Landing_WithCurrentPeriod_CountsSubjectsAndStudents()
    {
        using var s = new StudentsTestScope("landing-with-period");
        var periodId = await SeedCurrentPeriodAsync(s, "Term 1");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");

        // One subject assigned to this grade for the current period → SubjectCount 1.
        var subject = Subject.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        s.Db.Subjects.Add(subject);
        s.Db.GradeSubjectAssignments.Add(GradeSubjectAssignment.Create(glId, subject.Id, periodId));
        await s.Db.SaveChangesAsync();

        // One current-tenant student enrolled in this grade for the current period.
        var student = Student.Create("S1", "Anna", "Smith", null, null);
        student.WithTenant(s.Tenants); // current tenant (System/Empty)
        s.Db.Students.Add(student);
        s.Db.StudentEnrollments.Add(StudentEnrollment.Create(student.Id, periodId, glId));
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListGradeLevelsForLanding());

        result.Should().ContainSingle();
        var row = result[0];
        row.SubjectCount.Should().Be(1);
        row.StudentCount.Should().Be(1);
        row.CurrentPeriodId.Should().Be(periodId);
        row.CurrentPeriodName.Should().Be("Term 1");
    }

    [TestMethod]
    public async Task Landing_StudentCount_IsTenantScoped()
    {
        using var s = new StudentsTestScope("landing-tenant-scope");
        var periodId = await SeedCurrentPeriodAsync(s, "Term 1");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");

        // Current-tenant student → counted.
        var s1 = Student.Create("S1", "Anna", "Smith", null, null)
            .WithTenant(s.Tenants);
        s.Db.Students.Add(s1);
        s.Db.StudentEnrollments.Add(StudentEnrollment.Create(s1.Id, periodId, glId));

        // Other-tenant student → NOT counted (tenant query filter excludes them).
        var otherTenant = Guid.NewGuid();
        var s2 = Student.Create("S2", "Bob", "Jones", null, null)
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

        var student = Student.Create("S1", "Anna", "Smith", null, null)
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
}