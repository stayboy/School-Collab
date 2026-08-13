using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.CreateTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacherActivityAssignment;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacherGradeAssignment;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherActivityAssignment;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherGradeLevel;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeacherActivityAssignments;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeacherGradeAssignments;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// CQRS tests for the v4 teaching-assignment endpoints: grade-scoped subject rows
/// (grade + optional subject + role on <see cref="TeacherGradeLevel"/>) and
/// activity assignments (activity + role + optional grades on
/// <see cref="TeacherActivityAssignment"/>). The save-path contract the bUnit
/// dialog tests cannot drive (FluentButton submit is a web component) is covered
/// here at the handler level.
/// </summary>
[TestClass]
public class TeacherAssignmentCqrsTests
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
    private static LinkTeacherGradeLevelHandler NewLinkGrade(StudentsTestScope s) =>
        new(TeacherRepo(s), s.GradeLevels, s.Cache, s.Tenants, NullLogger<LinkTeacherGradeLevelHandler>.Instance);
    private static DeleteTeacherGradeAssignmentHandler NewDeleteGrade(StudentsTestScope s) =>
        new(TeacherRepo(s), s.Cache, s.Tenants, NullLogger<DeleteTeacherGradeAssignmentHandler>.Instance);
    private static ListTeacherGradeAssignmentsHandler NewListGrades(StudentsTestScope s) =>
        new(s.Db, s.Cache);
    private static LinkTeacherActivityAssignmentHandler NewLinkActivity(StudentsTestScope s) =>
        new(TeacherRepo(s), s.Db, s.Cache, s.Tenants, NullLogger<LinkTeacherActivityAssignmentHandler>.Instance);
    private static DeleteTeacherActivityAssignmentHandler NewDeleteActivity(StudentsTestScope s) =>
        new(TeacherRepo(s), s.Cache, s.Tenants, NullLogger<DeleteTeacherActivityAssignmentHandler>.Instance);
    private static ListTeacherActivityAssignmentsHandler NewListActivities(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    private static async Task<Guid> SeedTeacherAsync(StudentsTestScope s) =>
        await NewCreate(s).HandleAsync(new CreateTeacher(null, "Jane", "Doe", null));

    private static async Task<Guid> SeedGradeAsync(StudentsTestScope s, int level, string name)
    {
        var gl = GradeLevel.Create(Guid.NewGuid(), level, name, level).WithTenant(s.Tenants);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    private static async Task<Guid> SeedTopicAsync(StudentsTestScope s, string name, string code)
    {
        var topic = Topic.Create(Guid.NewGuid(), code, name, 0).WithTenant(s.Tenants);
        s.Db.Topics.Add(topic);
        await s.Db.SaveChangesAsync();
        return topic.Id;
    }

    private static async Task<Guid> SeedActivityAsync(StudentsTestScope s, string name)
    {
        var group = ActivityGroup.Create(name).WithTenant(s.Tenants);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        return group.Id;
    }

    // ── Grade-scoped subject rows ──────────────────────────────────────────

    [TestMethod]
    public async Task LinkGradeAssignment_WithSubject_CreatesRow()
    {
        using var s = new StudentsTestScope("grade-assign-subject");
        var teacherId = await SeedTeacherAsync(s);
        var gradeId = await SeedGradeAsync(s, 5, "Grade 5");
        var topicId = await SeedTopicAsync(s, "Mathematics", "MATH");
        var roleId = Guid.NewGuid();

        await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(teacherId, gradeId, topicId, roleId));

        var rows = await NewListGrades(s).HandleAsync(new ListTeacherGradeAssignments(teacherId));
        var row = rows.Should().ContainSingle().Subject;
        row.GradeLevelId.Should().Be(gradeId);
        row.GradeName.Should().Be("Grade 5");
        row.SubjectId.Should().Be(topicId);
        row.SubjectName.Should().Be("Mathematics");
        row.SubjectCode.Should().Be("MATH");
        row.RoleCodedValueId.Should().Be(roleId);
    }

    [TestMethod]
    public async Task LinkGradeAssignment_GradeOnly_CreatesRow()
    {
        using var s = new StudentsTestScope("grade-assign-gradeonly");
        var teacherId = await SeedTeacherAsync(s);
        var gradeId = await SeedGradeAsync(s, 5, "Grade 5");

        await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(teacherId, gradeId));

        var rows = await NewListGrades(s).HandleAsync(new ListTeacherGradeAssignments(teacherId));
        var row = rows.Should().ContainSingle().Subject;
        row.SubjectId.Should().BeNull();
        row.SubjectName.Should().BeNull();
    }

    [TestMethod]
    public async Task LinkGradeAssignment_MultipleSubjectsInSameGrade_Allowed()
    {
        using var s = new StudentsTestScope("grade-assign-multi");
        var teacherId = await SeedTeacherAsync(s);
        var gradeId = await SeedGradeAsync(s, 5, "Grade 5");
        var mathId = await SeedTopicAsync(s, "Mathematics", "MATH");
        var sciId = await SeedTopicAsync(s, "Science", "SCI");

        await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(teacherId, gradeId, mathId));
        await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(teacherId, gradeId, sciId));

        var rows = await NewListGrades(s).HandleAsync(new ListTeacherGradeAssignments(teacherId));
        rows.Should().HaveCount(2);
        rows.Select(r => r.SubjectName).Should().Contain(new[] { "Mathematics", "Science" });
    }

    [TestMethod]
    public async Task LinkGradeAssignment_MissingTeacher_Throws()
    {
        using var s = new StudentsTestScope("grade-assign-missing-teacher");
        var gradeId = await SeedGradeAsync(s, 5, "Grade 5");
        var act = async () => await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(Guid.NewGuid(), gradeId));
        await act.Should().ThrowAsync<TeacherNotFoundException>();
    }

    [TestMethod]
    public async Task LinkGradeAssignment_MissingGrade_Throws()
    {
        using var s = new StudentsTestScope("grade-assign-missing-grade");
        var teacherId = await SeedTeacherAsync(s);
        var act = async () => await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(teacherId, Guid.NewGuid()));
        await act.Should().ThrowAsync<GradeLevelNotFoundException>();
    }

    [TestMethod]
    public async Task LinkGradeAssignment_DuplicateRow_Throws()
    {
        using var s = new StudentsTestScope("grade-assign-duplicate");
        var teacherId = await SeedTeacherAsync(s);
        var gradeId = await SeedGradeAsync(s, 5, "Grade 5");
        var topicId = await SeedTopicAsync(s, "Mathematics", "MATH");

        await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(teacherId, gradeId, topicId));
        var act = async () => await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(teacherId, gradeId, topicId));
        await act.Should().ThrowAsync<TeacherLinkAlreadyExistsException>();
    }

    [TestMethod]
    public async Task DeleteGradeAssignment_RemovesRow()
    {
        using var s = new StudentsTestScope("grade-assign-delete");
        var teacherId = await SeedTeacherAsync(s);
        var gradeId = await SeedGradeAsync(s, 5, "Grade 5");
        var topicId = await SeedTopicAsync(s, "Mathematics", "MATH");
        await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(teacherId, gradeId, topicId));
        var rowId = (await NewListGrades(s).HandleAsync(new ListTeacherGradeAssignments(teacherId))).Single().RowId;

        await NewDeleteGrade(s).HandleAsync(new DeleteTeacherGradeAssignment(teacherId, rowId));

        var rows = await NewListGrades(s).HandleAsync(new ListTeacherGradeAssignments(teacherId));
        rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DeleteGradeAssignment_WrongTeacher_Throws()
    {
        using var s = new StudentsTestScope("grade-assign-delete-wrong");
        var teacherId = await SeedTeacherAsync(s);
        var otherId = await SeedTeacherAsync(s);
        var gradeId = await SeedGradeAsync(s, 5, "Grade 5");
        await NewLinkGrade(s).HandleAsync(new LinkTeacherGradeLevel(teacherId, gradeId));
        var rowId = (await NewListGrades(s).HandleAsync(new ListTeacherGradeAssignments(teacherId))).Single().RowId;

        var act = async () => await NewDeleteGrade(s).HandleAsync(new DeleteTeacherGradeAssignment(otherId, rowId));
        await act.Should().ThrowAsync<TeacherLinkNotFoundException>();
    }

    [TestMethod]
    public async Task DeleteGradeAssignment_MissingRow_Throws()
    {
        using var s = new StudentsTestScope("grade-assign-delete-missing");
        var teacherId = await SeedTeacherAsync(s);
        var act = async () => await NewDeleteGrade(s).HandleAsync(new DeleteTeacherGradeAssignment(teacherId, Guid.NewGuid()));
        await act.Should().ThrowAsync<TeacherLinkNotFoundException>();
    }

    // ── Activity assignments ────────────────────────────────────────────────

    [TestMethod]
    public async Task LinkActivityAssignment_CreatesRow_WithGrades()
    {
        using var s = new StudentsTestScope("activity-assign-grades");
        var teacherId = await SeedTeacherAsync(s);
        var activityId = await SeedActivityAsync(s, "Chess Club");
        var gradeA = await SeedGradeAsync(s, 5, "Grade 5");
        var gradeB = await SeedGradeAsync(s, 6, "Grade 6");
        var roleId = Guid.NewGuid();

        await NewLinkActivity(s).HandleAsync(new LinkTeacherActivityAssignment(teacherId, activityId, roleId, new[] { gradeA, gradeB }));

        var rows = await NewListActivities(s).HandleAsync(new ListTeacherActivityAssignments(teacherId));
        var row = rows.Should().ContainSingle().Subject;
        row.ActivityGroupId.Should().Be(activityId);
        row.ActivityName.Should().Be("Chess Club");
        row.RoleCodedValueId.Should().Be(roleId);
        row.GradeLevelIds.Should().BeEquivalentTo(new[] { gradeA, gradeB });
    }

    [TestMethod]
    public async Task LinkActivityAssignment_NoGrades_CreatesRow()
    {
        using var s = new StudentsTestScope("activity-assign-nogrades");
        var teacherId = await SeedTeacherAsync(s);
        var activityId = await SeedActivityAsync(s, "Chess Club");

        await NewLinkActivity(s).HandleAsync(new LinkTeacherActivityAssignment(teacherId, activityId));

        var rows = await NewListActivities(s).HandleAsync(new ListTeacherActivityAssignments(teacherId));
        rows.Should().ContainSingle().Subject.GradeLevelIds.Should().BeEmpty();
    }

    [TestMethod]
    public async Task LinkActivityAssignment_MissingTeacher_Throws()
    {
        using var s = new StudentsTestScope("activity-assign-missing-teacher");
        var activityId = await SeedActivityAsync(s, "Chess Club");
        var act = async () => await NewLinkActivity(s).HandleAsync(new LinkTeacherActivityAssignment(Guid.NewGuid(), activityId));
        await act.Should().ThrowAsync<TeacherNotFoundException>();
    }

    [TestMethod]
    public async Task LinkActivityAssignment_MissingActivity_Throws()
    {
        using var s = new StudentsTestScope("activity-assign-missing-activity");
        var teacherId = await SeedTeacherAsync(s);
        var act = async () => await NewLinkActivity(s).HandleAsync(new LinkTeacherActivityAssignment(teacherId, Guid.NewGuid()));
        await act.Should().ThrowAsync<ActivityGroupNotFoundException>();
    }

    [TestMethod]
    public async Task DeleteActivityAssignment_RemovesRow_AndGrades()
    {
        using var s = new StudentsTestScope("activity-assign-delete");
        var teacherId = await SeedTeacherAsync(s);
        var activityId = await SeedActivityAsync(s, "Chess Club");
        var gradeA = await SeedGradeAsync(s, 5, "Grade 5");
        await NewLinkActivity(s).HandleAsync(new LinkTeacherActivityAssignment(teacherId, activityId, null, new[] { gradeA }));
        var rowId = (await NewListActivities(s).HandleAsync(new ListTeacherActivityAssignments(teacherId))).Single().RowId;

        await NewDeleteActivity(s).HandleAsync(new DeleteTeacherActivityAssignment(teacherId, rowId));

        var rows = await NewListActivities(s).HandleAsync(new ListTeacherActivityAssignments(teacherId));
        rows.Should().BeEmpty();
        s.Db.TeacherActivityAssignmentGrades.IgnoreQueryFilters().Should().NotContain(g => g.TeacherActivityAssignmentId == rowId,
            "the join grades are removed with the assignment row");
    }

    [TestMethod]
    public async Task DeleteActivityAssignment_WrongTeacher_Throws()
    {
        using var s = new StudentsTestScope("activity-assign-delete-wrong");
        var teacherId = await SeedTeacherAsync(s);
        var otherId = await SeedTeacherAsync(s);
        var activityId = await SeedActivityAsync(s, "Chess Club");
        await NewLinkActivity(s).HandleAsync(new LinkTeacherActivityAssignment(teacherId, activityId));
        var rowId = (await NewListActivities(s).HandleAsync(new ListTeacherActivityAssignments(teacherId))).Single().RowId;

        var act = async () => await NewDeleteActivity(s).HandleAsync(new DeleteTeacherActivityAssignment(otherId, rowId));
        await act.Should().ThrowAsync<TeacherLinkNotFoundException>();
    }

    [TestMethod]
    public async Task DeleteActivityAssignment_MissingRow_Throws()
    {
        using var s = new StudentsTestScope("activity-assign-delete-missing");
        var teacherId = await SeedTeacherAsync(s);
        var act = async () => await NewDeleteActivity(s).HandleAsync(new DeleteTeacherActivityAssignment(teacherId, Guid.NewGuid()));
        await act.Should().ThrowAsync<TeacherLinkNotFoundException>();
    }
}
