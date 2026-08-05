using FluentAssertions;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Students.Queries.GetStudentById;
using SchoolCollab.Students.Core.CQRS.Students.Queries.GetStudentByStudentNumber;
using SchoolCollab.Students.Core.CQRS.Students.Queries.ListStudents;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class StudentTenancyQueryHandlerTests
{
    private static GetStudentByIdHandler NewByIdHandler(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    private static GetStudentByStudentNumberHandler NewByNumberHandler(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    private static ListStudentsHandler NewListHandler(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    private static async Task<Student> SeedStudentAsync(
        StudentsTestScope s,
        string studentNumber,
        string firstName,
        string lastName,
        Guid? tenantId = null)
    {
        var student = Student.Create(
            studentNumber,
            firstName,
            lastName,
            new DateOnly(2015, 1, 1),
            Guid.NewGuid());

        if (tenantId is { } otherTenantId)
        {
            student.WithTenant(otherTenantId);
            s.Db.Students.Add(student);
            using (s.TenantAccessor.SuppressTenantGuard())
            {
                await s.Db.SaveChangesAsync();
            }
        }
        else
        {
            student.WithTenant(s.Tenants);
            s.Db.Students.Add(student);
            await s.Db.SaveChangesAsync();
        }

        return student;
    }

    [TestMethod]
    public async Task GetStudentById_ReturnsNull_ForStudentFromAnotherTenant()
    {
        using var s = new StudentsTestScope("student-by-id-tenant-scope");
        var otherTenantStudent = await SeedStudentAsync(
            s,
            "S-OTHER-1",
            "Other",
            "Tenant",
            Guid.NewGuid());

        var result = await NewByIdHandler(s).HandleAsync(new GetStudentById(otherTenantStudent.Id));

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task GetStudentByStudentNumber_ReturnsCurrentTenantMatch_WhenSameNumberExistsInTwoTenants()
    {
        using var s = new StudentsTestScope("student-by-number-tenant-scope");
        await SeedStudentAsync(s, "S-100", "Anna", "Current");
        await SeedStudentAsync(s, "S-100", "Olivia", "Other", Guid.NewGuid());

        var result = await NewByNumberHandler(s).HandleAsync(new GetStudentByStudentNumber(" s-100 "));

        result.Should().NotBeNull();
        result!.FirstName.Should().Be("Anna");
        result.LastName.Should().Be("Current");
        result.StudentNumber.Should().Be("S-100");
    }

    [TestMethod]
    public async Task GetStudentByStudentNumber_ReturnsNull_WhenMatchExistsOnlyInAnotherTenant()
    {
        using var s = new StudentsTestScope("student-by-number-other-tenant-only");
        await SeedStudentAsync(s, "S-404", "Other", "Tenant", Guid.NewGuid());

        var result = await NewByNumberHandler(s).HandleAsync(new GetStudentByStudentNumber("S-404"));

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ListStudents_ReturnsOnlyCurrentTenantStudents()
    {
        using var s = new StudentsTestScope("list-students-tenant-scope");
        await SeedStudentAsync(s, "S-001", "Anna", "Current");
        await SeedStudentAsync(s, "S-002", "Zoe", "Current");
        await SeedStudentAsync(s, "S-003", "Other", "Tenant", Guid.NewGuid());

        var result = await NewListHandler(s).HandleAsync(new ListStudents());

        result.Select(x => x.StudentNumber).Should().BeEquivalentTo(["S-001", "S-002"]);
        result.Should().OnlyContain(x => x.LastName == "Current");
    }

    [TestMethod]
    public async Task ListStudents_Search_IsStillTenantScoped()
    {
        using var s = new StudentsTestScope("list-students-search-tenant-scope");
        await SeedStudentAsync(s, "S-100", "Alice", "Visible");
        await SeedStudentAsync(s, "S-200", "Alice", "Hidden", Guid.NewGuid());

        var result = await NewListHandler(s).HandleAsync(new ListStudents("Alice"));

        result.Should().ContainSingle();
        result[0].LastName.Should().Be("Visible");
        result[0].StudentNumber.Should().Be("S-100");
    }
}
