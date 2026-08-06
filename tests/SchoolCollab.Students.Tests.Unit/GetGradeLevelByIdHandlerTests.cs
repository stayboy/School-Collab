using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.GetGradeLevelById;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class GetGradeLevelByIdHandlerTests
{
    private static GetGradeLevelByIdHandler NewHandler(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s, string name = "Grade 1")
    {
        var gl = GradeLevel.Create(Guid.NewGuid(), 1, name, 1);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    private static async Task<Guid> SeedCurrentPeriodAsync(StudentsTestScope s, string name = "Term 1")
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var period = Period.Create(name, today.AddDays(-1), today.AddDays(1));
        s.Db.Periods.Add(period);
        await s.Db.SaveChangesAsync();
        return period.Id;
    }

    private static async Task<Guid> SeedEnrolledStudentAsync(
        StudentsTestScope s, Guid glId, Guid periodId, string studentNumber)
    {
        var student = Student.Create(studentNumber, "Anna", "Smith", new DateOnly(2015, 1, 1), Guid.NewGuid())
            .WithTenant(s.Tenants);
        s.Db.Students.Add(student);
        s.Db.StudentEnrollments.Add(StudentEnrollment.Create(student.Id, periodId, glId));
        await s.Db.SaveChangesAsync();
        return student.Id;
    }

    // Regression: GetGradeLevelById previously hardcoded StudentCount = 0, so the
    // grade-detail Overview always rendered "0 students". It must reflect the real,
    // tenant-scoped enrollment count for the grade.
    [TestMethod]
    public async Task Detail_StudentCount_ReflectsEnrollments()
    {
        using var s = new StudentsTestScope("detail-count-real");
        var glId = await SeedGradeLevelAsync(s);
        var periodId = await SeedCurrentPeriodAsync(s);
        await SeedEnrolledStudentAsync(s, glId, periodId, "S1");
        await SeedEnrolledStudentAsync(s, glId, periodId, "S2");

        var result = await NewHandler(s).HandleAsync(new GetGradeLevelById(glId));

        result.Should().NotBeNull();
        result!.StudentCount.Should().Be(2);
        result.Id.Should().Be(glId);
    }

    [TestMethod]
    public async Task Detail_StudentCount_Zero_WhenNoneEnrolled()
    {
        using var s = new StudentsTestScope("detail-count-none");
        var glId = await SeedGradeLevelAsync(s);

        var result = await NewHandler(s).HandleAsync(new GetGradeLevelById(glId));

        result.Should().NotBeNull();
        result!.StudentCount.Should().Be(0);
    }

    [TestMethod]
    public async Task Detail_StudentCount_IsTenantScoped()
    {
        using var s = new StudentsTestScope("detail-count-tenant");
        var glId = await SeedGradeLevelAsync(s);
        var periodId = await SeedCurrentPeriodAsync(s);

        // Current-tenant student → counted.
        await SeedEnrolledStudentAsync(s, glId, periodId, "S1");

        // Other-tenant student → NOT counted.
        var otherTenant = Guid.NewGuid();
        var other = Student.Create("S2", "Bob", "Jones", new DateOnly(2015, 1, 1), Guid.NewGuid())
            .WithTenant(otherTenant);
        s.Db.Students.Add(other);
        s.Db.StudentEnrollments.Add(StudentEnrollment.Create(other.Id, periodId, glId));
        using (s.TenantAccessor.SuppressTenantGuard())
        {
            await s.Db.SaveChangesAsync();
        }

        var result = await NewHandler(s).HandleAsync(new GetGradeLevelById(glId));

        result.Should().NotBeNull();
        result!.StudentCount.Should().Be(1, "only the current tenant's enrollments are counted");
    }
}
