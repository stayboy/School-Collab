using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.CreateTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherGradeLevel;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherGradeLevel;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.UpdateTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.GetTeacherById;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListGradeLevelsForTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeachers;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class TeacherCqrsTests
{
    private static TeacherRepository TeacherRepo(StudentsTestScope s) => new(s.Db);
    private static CreateTeacherHandler NewCreate(StudentsTestScope s) =>
        new(TeacherRepo(s), NewEntityCodeGenerator(), s.Cache, s.Tenants, NullLogger<CreateTeacherHandler>.Instance);
    private static IEntityCodeGenerator NewEntityCodeGenerator()
    {
        var mock = new Mock<IEntityCodeGenerator>();
        mock.Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("STFA01");
        return mock.Object;
    }
    private static UpdateTeacherHandler NewUpdate(StudentsTestScope s) =>
        new(TeacherRepo(s), s.Cache, NullLogger<UpdateTeacherHandler>.Instance);
    private static DeleteTeacherHandler NewDelete(StudentsTestScope s) =>
        new(TeacherRepo(s), s.Cache, NullLogger<DeleteTeacherHandler>.Instance);
    private static LinkTeacherGradeLevelHandler NewLinkGrade(StudentsTestScope s) =>
        new(TeacherRepo(s), s.GradeLevels, s.Cache, s.Tenants, NullLogger<LinkTeacherGradeLevelHandler>.Instance);
    private static UnlinkTeacherGradeLevelHandler NewUnlinkGrade(StudentsTestScope s) =>
        new(TeacherRepo(s), s.Cache, NullLogger<UnlinkTeacherGradeLevelHandler>.Instance);
    private static GetTeacherByIdHandler NewGetById(StudentsTestScope s) =>
        new(TeacherRepo(s));
    private static ListTeachersHandler NewList(StudentsTestScope s) =>
        new(s.Db, s.Cache);
    private static ListGradeLevelsForTeacherHandler NewListGrades(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s, int level, string name)
    {
        var gl = GradeLevel.Create(Guid.NewGuid(), level, name, level).WithTenant(s.Tenants);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    [TestMethod]
    public async Task CreateTeacher_CreatesTeacher_WithTenant()
    {
        using var s = new StudentsTestScope("teacher-create");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));

        var teacher = s.Db.Teachers.IgnoreQueryFilters().Single(t => t.Id == id);
        teacher.FirstName.Should().Be("Jane");
        teacher.LastName.Should().Be("Doe");
        teacher.TenantId.Should().Be(s.Tenants.GetTenantContext().TenantId);
        teacher.IsDeleted.Should().BeFalse();
    }

    [TestMethod]
    public async Task UpdateTeacher_ChangesFields()
    {
        using var s = new StudentsTestScope("teacher-update");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));

        await NewUpdate(s).HandleAsync(new UpdateTeacher(id, "Janet", "Smith", "Ms. Doe"));

        var teacher = s.Db.Teachers.IgnoreQueryFilters().Single(t => t.Id == id);
        teacher.FirstName.Should().Be("Janet");
        teacher.LastName.Should().Be("Smith");
        teacher.DisplayName.Should().Be("Ms. Doe");
    }

    [TestMethod]
    public async Task UpdateTeacher_MissingTeacher_Throws()
    {
        using var s = new StudentsTestScope("teacher-update-missing");
        var act = async () => await NewUpdate(s).HandleAsync(new UpdateTeacher(Guid.NewGuid(), "A", "B", null));
        await act.Should().ThrowAsync<TeacherNotFoundException>();
    }

    [TestMethod]
    public async Task DeleteTeacher_SoftDeletes()
    {
        using var s = new StudentsTestScope("teacher-delete");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));

        await NewDelete(s).HandleAsync(new DeleteTeacher(id));

        var teacher = s.Db.Teachers.IgnoreQueryFilters().Single(t => t.Id == id);
        teacher.IsDeleted.Should().BeTrue();
        teacher.DeletedAt.Should().NotBeNull();

        // ListTeachers (tenant-filtered) should exclude the soft-deleted teacher.
        var listed = await NewList(s).HandleAsync(new ListTeachers());
        listed.Should().NotContain(t => t.Id == id);
    }

    [TestMethod]
    public async Task LinkAndUnlinkGradeLevel_PersistsLink()
    {
        using var s = new StudentsTestScope("teacher-grade");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));
        var gradeId = await SeedGradeLevelAsync(s, 5, "Grade 5");

        await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(id, gradeId));
        var gradeLinks = await NewListGrades(s).HandleAsync(new ListGradeLevelsForTeacher(id));
        gradeLinks.Should().ContainSingle(x => x.Id == gradeId);

        await NewUnlinkGrade(s).HandleAsync(new UnlinkTeacherGradeLevel(id, gradeId));
        gradeLinks = await NewListGrades(s).HandleAsync(new ListGradeLevelsForTeacher(id));
        gradeLinks.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetTeacherById_ReturnsMappedDto()
    {
        using var s = new StudentsTestScope("teacher-get");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(Guid.NewGuid(), "Jane", "Doe", "Ms. Doe"));

        var dto = await NewGetById(s).HandleAsync(new GetTeacherById(id));
        dto.Should().NotBeNull();
        dto!.FirstName.Should().Be("Jane");
        dto!.LastName.Should().Be("Doe");
        dto!.DisplayName.Should().Be("Ms. Doe");
        dto!.IsDeleted.Should().BeFalse();
    }

    [TestMethod]
    public async Task ListTeachers_ReturnsAllForTenant()
    {
        using var s = new StudentsTestScope("teacher-list");
        await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));
        await NewCreate(s).HandleAsync(new CreateTeacher(null, "John", "Roe", null));

        var listed = await NewList(s).HandleAsync(new ListTeachers());
        listed.Should().HaveCount(2);
        listed.Select(t => t.LastName).Should().Contain(new[] { "Doe", "Roe" });
    }

    [TestMethod]
    public async Task LinkGradeLevel_DuplicateLink_Throws()
    {
        using var s = new StudentsTestScope("teacher-grade-duplicate");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));
        var gradeId = await SeedGradeLevelAsync(s, 5, "Grade 5");

        await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(id, gradeId));
        var act = async () => await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(id, gradeId));
        await act.Should().ThrowAsync<TeacherLinkAlreadyExistsException>();
    }

    [TestMethod]
    public async Task LinkGradeLevel_ReturnsFullGradeDetails()
    {
        using var s = new StudentsTestScope("teacher-grade-details");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));

        // Create a grade level with specific enrollment constraints
        var genderId = Guid.NewGuid();
        var gl = GradeLevel.Create(
            Guid.NewGuid(),
            level: 3,
            name: "Grade 3",
            displayOrder: 3,
            minAge: 8,
            maxAge: 9,
            allowedGenderCodedValueId: genderId,
            isBlockedFromEnrollment: false)
            .WithTenant(s.Tenants);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();

        // Link the teacher to this grade level
        await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(id, gl.Id));

        // Query the grade levels for this teacher
        var gradeLinks = await NewListGrades(s).HandleAsync(new ListGradeLevelsForTeacher(id));

        // Verify the grade level details are correctly returned
        gradeLinks.Should().ContainSingle(x => x.Id == gl.Id);
        var linkedGrade = gradeLinks.Single(x => x.Id == gl.Id);
        linkedGrade.Level.Should().Be(3);
        linkedGrade.Name.Should().Be("Grade 3");
        linkedGrade.DisplayOrder.Should().Be(3);
        linkedGrade.MinAge.Should().Be(8);
        linkedGrade.MaxAge.Should().Be(9);
        linkedGrade.AllowedGenderCodedValueId.Should().Be(genderId);
        linkedGrade.IsBlockedFromEnrollment.Should().BeFalse();
        linkedGrade.TopicCount.Should().Be(0);
        linkedGrade.StudentCount.Should().Be(0);
    }
}
