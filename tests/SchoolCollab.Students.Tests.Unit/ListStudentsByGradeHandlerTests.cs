using FluentAssertions;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Students.Queries.ListStudentsByGrade;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ListStudentsByGradeHandler"/> — returns the students
/// enrolled (Active) in a grade for a period, tenant-scoped via the Student query
/// filter, ordered by LastName then FirstName. This handler is exercised on the
/// EF Core InMemory provider so a client-side projection/join regression surfaces
/// here instead of at runtime.
/// </summary>
[TestClass]
public class ListStudentsByGradeHandlerTests
{
    private static readonly Guid GradeCodedValueId = Guid.Parse("22222222-2222-2222-2222-222222222223");
    private static readonly Guid GenderMale = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid GenderFemale = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid GradeLevelId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PeriodId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static ListStudentsByGradeHandler NewHandler(StudentsTestScope s)
        => new(s.Db, s.Tenants);

    private static (Student student, StudentEnrollment enrollment) Seed(
        StudentsTestScope s,
        string number,
        string firstName,
        string lastName,
        Guid tenant)
    {
        var student = Student.Create(number, firstName, lastName, new DateOnly(2015, 1, 1), GenderMale)
            .WithTenant(tenant);
        s.Db.Students.Add(student);
        // Suppress the save-guard so the other-tenant seeding in
        // IsTenantScoped_ExcludesOtherTenantStudents is allowed (FR-6 bypass).
        using (s.TenantAccessor.SuppressTenantGuard())
        {
            s.Db.SaveChanges();
        }

        var enrollment = StudentEnrollment.Create(student.Id, PeriodId, GradeLevelId);
        s.Db.StudentEnrollments.Add(enrollment);
        using (s.TenantAccessor.SuppressTenantGuard())
        {
            s.Db.SaveChanges();
        }
        return (student, enrollment);
    }

    private static Student SeedCurrentTenant(StudentsTestScope s, string number, string firstName, string lastName)
    {
        var (student, _) = Seed(s, number, firstName, lastName, s.Tenants.GetTenantContext().TenantId);
        return student;
    }

    [TestMethod]
    public async Task ReturnsActiveStudentsInGrade_ForExplicitPeriod_OrderedByName()
    {
        using var s = new StudentsTestScope("list-students-by-grade-basic");
        SeedCurrentTenant(s, "S1", "Anna", "Smith");
        SeedCurrentTenant(s, "S2", "Bob", "Jones");

        var result = await NewHandler(s).HandleAsync(new ListStudentsByGrade(GradeLevelId, PeriodId));

        result.Should().HaveCount(2);
        result.Select(x => x.StudentNumber).Should().BeEquivalentTo(new[] { "S2", "S1" });
        result.Select(x => x.LastName).Should().BeInAscendingOrder();
    }

    [TestMethod]
    public async Task ReturnsEmpty_WhenPeriodHasNoEnrollments()
    {
        using var s = new StudentsTestScope("list-students-by-grade-empty");
        SeedCurrentTenant(s, "S1", "Anna", "Smith");

        var result = await NewHandler(s).HandleAsync(new ListStudentsByGrade(GradeLevelId, Guid.NewGuid()));

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task IsTenantScoped_ExcludesOtherTenantStudents()
    {
        using var s = new StudentsTestScope("list-students-by-grade-tenant");
        SeedCurrentTenant(s, "S1", "Anna", "Smith");
        Seed(s, "S2", "Bob", "Jones", Guid.NewGuid()); // other tenant

        var result = await NewHandler(s).HandleAsync(new ListStudentsByGrade(GradeLevelId, PeriodId));

        result.Should().ContainSingle();
        result[0].StudentNumber.Should().Be("S1");
    }

    [TestMethod]
    public async Task ReturnsEmpty_WhenNoCurrentPeriod()
    {
        using var s = new StudentsTestScope("list-students-by-grade-noperiod");
        SeedCurrentTenant(s, "S1", "Anna", "Smith");

        // No Period row in the in-memory store → periodId stays null → empty result.
        var result = await NewHandler(s).HandleAsync(new ListStudentsByGrade(GradeLevelId));

        result.Should().BeEmpty();
    }
}
