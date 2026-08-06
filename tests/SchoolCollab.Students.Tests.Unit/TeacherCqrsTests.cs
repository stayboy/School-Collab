using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.CreateTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherGradeLevel;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherTopic;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.SetTeacherTopicRole;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherGradeLevel;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherTopic;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.UpdateTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.GetTeacherById;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListGradeLevelsForTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTopicTeachers;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTopicsForTeacher;
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
    private static LinkTeacherTopicHandler NewLinkTopic(StudentsTestScope s) =>
        new(TeacherRepo(s), s.Topics, s.Cache, s.Tenants, NullLogger<LinkTeacherTopicHandler>.Instance);
    private static UnlinkTeacherTopicHandler NewUnlinkTopic(StudentsTestScope s) =>
        new(TeacherRepo(s), s.Cache, NullLogger<UnlinkTeacherTopicHandler>.Instance);
    private static SetTeacherTopicRoleHandler NewSetTopicRole(StudentsTestScope s) =>
        new(TeacherRepo(s), s.Cache, s.Tenants, NullLogger<SetTeacherTopicRoleHandler>.Instance);
    private static ListTopicTeachersHandler NewListTopicTeachers(StudentsTestScope s) =>
        new(s.Db, s.Cache);
    private static LinkTeacherGradeLevelHandler NewLinkGrade(StudentsTestScope s) =>
        new(TeacherRepo(s), s.GradeLevels, s.Cache, s.Tenants, NullLogger<LinkTeacherGradeLevelHandler>.Instance);
    private static UnlinkTeacherGradeLevelHandler NewUnlinkGrade(StudentsTestScope s) =>
        new(TeacherRepo(s), s.Cache, NullLogger<UnlinkTeacherGradeLevelHandler>.Instance);
    private static GetTeacherByIdHandler NewGetById(StudentsTestScope s) =>
        new(TeacherRepo(s));
    private static ListTeachersHandler NewList(StudentsTestScope s) =>
        new(s.Db, s.Cache);
    private static ListTopicsForTeacherHandler NewListTopics(StudentsTestScope s) =>
        new(s.Db, s.Cache);
    private static ListGradeLevelsForTeacherHandler NewListGrades(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    private static async Task<Guid> SeedTopicAsync(StudentsTestScope s, string code, string name)
    {
        var topic = Topic.Create(Guid.NewGuid(), code, name, 1).WithTenant(s.Tenants);
        s.Db.Topics.Add(topic);
        await s.Db.SaveChangesAsync();
        return topic.Id;
    }

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
    public async Task LinkAndUnlinkTopic_PersistsLink()
    {
        using var s = new StudentsTestScope("teacher-subject");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));
        var subjectId = await SeedTopicAsync(s, "MATH", "Mathematics");

        await NewLinkTopic(s).HandleAsync(new LinkTeacherTopic(id, subjectId));
        var subjectLinks = await NewListTopics(s).HandleAsync(new ListTopicsForTeacher(id));
        subjectLinks.Should().ContainSingle(x => x.Id == subjectId);

        await NewUnlinkTopic(s).HandleAsync(new UnlinkTeacherTopic(id, subjectId));
        subjectLinks = await NewListTopics(s).HandleAsync(new ListTopicsForTeacher(id));
        subjectLinks.Should().BeEmpty();
    }

    [TestMethod]
    public async Task LinkTopic_WithRole_PersistsRole()
    {
        using var s = new StudentsTestScope("teacher-topic-role");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));
        var subjectId = await SeedTopicAsync(s, "MATH", "Mathematics");
        var roleId = Guid.NewGuid();

        await NewLinkTopic(s).HandleAsync(new LinkTeacherTopic(id, subjectId, roleId));

        var link = await TeacherRepo(s).GetTopicLinkAsync(id, subjectId);
        link.Should().NotBeNull();
        link!.RoleCodedValueId.Should().Be(roleId);

        // Re-linking the same pair without a role must still conflict.
        var act = async () => await NewLinkTopic(s).HandleAsync(new LinkTeacherTopic(id, subjectId));
        await act.Should().ThrowAsync<TeacherLinkAlreadyExistsException>();
    }

    [TestMethod]
    public async Task SetTopicRole_UpdatesRoleOnExistingLink()
    {
        using var s = new StudentsTestScope("teacher-topic-setrole");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));
        var topicId = await SeedTopicAsync(s, "MATH", "Mathematics");
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        await NewLinkTopic(s).HandleAsync(new LinkTeacherTopic(id, topicId, roleA));

        await NewSetTopicRole(s).HandleAsync(new SetTeacherTopicRole(id, topicId, roleB));

        var link = await TeacherRepo(s).GetTopicLinkAsync(id, topicId);
        link!.RoleCodedValueId.Should().Be(roleB);
    }

    [TestMethod]
    public async Task SetTopicRole_ClearsRole_WhenNull()
    {
        using var s = new StudentsTestScope("teacher-topic-clearrole");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));
        var topicId = await SeedTopicAsync(s, "MATH", "Mathematics");
        await NewLinkTopic(s).HandleAsync(new LinkTeacherTopic(id, topicId, Guid.NewGuid()));

        await NewSetTopicRole(s).HandleAsync(new SetTeacherTopicRole(id, topicId, null));

        var link = await TeacherRepo(s).GetTopicLinkAsync(id, topicId);
        link!.RoleCodedValueId.Should().BeNull();
    }

    [TestMethod]
    public async Task SetTopicRole_WithoutLink_ThrowsNotFound()
    {
        using var s = new StudentsTestScope("teacher-topic-setrole-missing");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));
        var topicId = await SeedTopicAsync(s, "MATH", "Mathematics");

        var act = async () => await NewSetTopicRole(s).HandleAsync(new SetTeacherTopicRole(id, topicId, Guid.NewGuid()));

        await act.Should().ThrowAsync<TeacherLinkNotFoundException>();
    }

    [TestMethod]
    public async Task ListTopicTeachers_ReturnsTeachersWithRoles()
    {
        using var s = new StudentsTestScope("list-topic-teachers");
        var topicId = await SeedTopicAsync(s, "MATH", "Mathematics");
        var idA = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));
        var idB = await NewCreate(s).HandleAsync(new CreateTeacher(null, "John", "Smith", null));
        var roleA = Guid.NewGuid();
        await NewLinkTopic(s).HandleAsync(new LinkTeacherTopic(idA, topicId, roleA));
        await NewLinkTopic(s).HandleAsync(new LinkTeacherTopic(idB, topicId));

        var result = await NewListTopicTeachers(s).HandleAsync(new ListTopicTeachers(topicId));

        result.Should().HaveCount(2);
        var jane = result.Should().ContainSingle(x => x.TeacherId == idA).Which;
        jane.RoleCodedValueId.Should().Be(roleA);
        var john = result.Should().ContainSingle(x => x.TeacherId == idB).Which;
        john.RoleCodedValueId.Should().BeNull();
        john.FirstName.Should().Be("John");
    }

    [TestMethod]
    public async Task ListTopicTeachers_UnlinkedTeacher_NotReturned()
    {
        using var s = new StudentsTestScope("list-topic-teachers-unlinked");
        var topicId = await SeedTopicAsync(s, "MATH", "Mathematics");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));

        var result = await NewListTopicTeachers(s).HandleAsync(new ListTopicTeachers(topicId));

        result.Should().BeEmpty();
        result.Should().NotContain(x => x.TeacherId == id);
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
    public async Task LinkTopic_MissingTeacher_Throws()
    {
        using var s = new StudentsTestScope("teacher-link-missing-teacher");
        var subjectId = await SeedTopicAsync(s, "MATH", "Mathematics");

        var act = async () => await NewLinkTopic(s).HandleAsync(new LinkTeacherTopic(Guid.NewGuid(), subjectId));
        await act.Should().ThrowAsync<TeacherNotFoundException>();
    }

    [TestMethod]
    public async Task LinkTopic_MissingTopic_Throws()
    {
        using var s = new StudentsTestScope("teacher-link-missing-subject");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));

        var act = async () => await NewLinkTopic(s).HandleAsync(new LinkTeacherTopic(id, Guid.NewGuid()));
        await act.Should().ThrowAsync<TopicNotFoundException>();
    }

    [TestMethod]
    public async Task LinkTopic_DuplicateLink_Throws()
    {
        using var s = new StudentsTestScope("teacher-link-duplicate");
        var id = await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));
        var subjectId = await SeedTopicAsync(s, "MATH", "Mathematics");

        await NewLinkTopic(s).HandleAsync(new LinkTeacherTopic(id, subjectId));
        var act = async () => await NewLinkTopic(s).HandleAsync(new LinkTeacherTopic(id, subjectId));
        await act.Should().ThrowAsync<TeacherLinkAlreadyExistsException>();
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
}
