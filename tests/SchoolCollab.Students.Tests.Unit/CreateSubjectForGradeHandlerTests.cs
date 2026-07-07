using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectForGrade;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class CreateSubjectForGradeHandlerTests
{
    private static CreateSubjectForGradeHandler NewHandler(StudentsTestScope s) =>
        new(
            s.Subjects,
            s.GradeSubjectAssignments,
            s.GradeLevels,
            s.Periods,
            s.Cache,
            NullLogger<CreateSubjectForGradeHandler>.Instance);

    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s, Guid codedValueId, int level, string name)
    {
        var gl = GradeLevel.Create(codedValueId, level, name, level);
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
    public async Task CreateForGrade_CreatesSubjectAndAssignment()
    {
        using var s = new StudentsTestScope("csfg-create");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        var periodId = await SeedCurrentPeriodAsync(s, "Term 1");
        var h = NewHandler(s);

        var dto = await h.HandleAsync(new CreateSubjectForGrade(gradeId, CodedValueId: null, "MATH", "Mathematics", 1));

        dto.Id.Should().NotBeEmpty();
        dto.Code.Should().Be("MATH");
        dto.Name.Should().Be("Mathematics");
        (await s.Db.Subjects.CountAsync()).Should().Be(1);
        (await s.Db.GradeSubjectAssignments.CountAsync()).Should().Be(1);
        var assignment = await s.Db.GradeSubjectAssignments.FirstAsync();
        assignment.GradeLevelId.Should().Be(gradeId);
        assignment.SubjectId.Should().Be(dto.Id);
        assignment.PeriodId.Should().Be(periodId);
    }

    [TestMethod]
    public async Task CreateForGrade_ReusesExistingSubjectByCodedValueId()
    {
        using var s = new StudentsTestScope("csfg-reuse");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        await SeedCurrentPeriodAsync(s, "Term 1");

        // Pre-existing subject with the same CodedValueId
        var existing = Subject.Create(cv, "ENG", "English", 2);
        s.Db.Subjects.Add(existing);
        await s.Db.SaveChangesAsync();

        var h = NewHandler(s);

        var dto = await h.HandleAsync(new CreateSubjectForGrade(gradeId, CodedValueId: cv, "ENG", "English Updated", 5));

        // Reuses the existing subject, updates mirrored name
        dto.Id.Should().Be(existing.Id);
        dto.Name.Should().Be("English Updated");
        (await s.Db.Subjects.CountAsync()).Should().Be(1);
        (await s.Db.GradeSubjectAssignments.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task CreateForGrade_IsIdempotentForAssignment()
    {
        using var s = new StudentsTestScope("csfg-idempotent");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        await SeedCurrentPeriodAsync(s, "Term 1");
        var h = NewHandler(s);

        await h.HandleAsync(new CreateSubjectForGrade(gradeId, null, "MATH", "Mathematics", 1));
        await h.HandleAsync(new CreateSubjectForGrade(gradeId, null, "MATH", "Mathematics", 1));

        // Same code → find-or-create reuses the same subject, and the assignment
        // is not duplicated.
        (await s.Db.Subjects.CountAsync()).Should().Be(1);
        (await s.Db.GradeSubjectAssignments.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task CreateForGrade_ThrowsWhenNoCurrentPeriod()
    {
        using var s = new StudentsTestScope("csfg-no-period");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        // No period seeded
        var h = NewHandler(s);

        var act = async () => await h.HandleAsync(new CreateSubjectForGrade(gradeId, null, "MATH", "Mathematics", 1));

        await act.Should().ThrowAsync<NoCurrentPeriodException>();
    }

    [TestMethod]
    public async Task CreateForGrade_ThrowsWhenGradeLevelNotFound()
    {
        using var s = new StudentsTestScope("csfg-no-grade");
        await SeedCurrentPeriodAsync(s, "Term 1");
        var h = NewHandler(s);

        var act = async () => await h.HandleAsync(new CreateSubjectForGrade(Guid.NewGuid(), null, "MATH", "Mathematics", 1));

        await act.Should().ThrowAsync<GradeLevelNotFoundException>();
    }
}