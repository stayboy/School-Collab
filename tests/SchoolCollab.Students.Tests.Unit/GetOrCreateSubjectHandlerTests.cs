using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.GetOrCreateTopic;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class GetOrCreateTopicHandlerTests
{
    private static GetOrCreateTopicHandler NewHandler(StudentsTestScope s) =>
        new(s.Topics, s.GradeSubjectAssignments, s.GradeLevels, s.Cache, s.Tenants, NullLogger<GetOrCreateTopicHandler>.Instance);

    [TestMethod]
    public async Task GetOrCreate_CreatesWhenAbsent()
    {
        using var s = new StudentsTestScope("gocs-create");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        await SeedCurrentPeriodAsync(s, "Term 1");
        var h = NewHandler(s);

        var dto = await h.HandleAsync(new GetOrCreateTopic(gradeId, cv, "MATH", "Mathematics", 1));

        dto.Id.Should().NotBeEmpty();
        dto.CodedValueId.Should().Be(cv);
        dto.Code.Should().Be("MATH");
        dto.Name.Should().Be("Mathematics");
        (await s.Db.Topics.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task GetOrCreate_ReusesAndUpdatesWhenPresent()
    {
        using var s = new StudentsTestScope("gocs-reuse");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        await SeedCurrentPeriodAsync(s, "Term 1");
        var h = NewHandler(s);

        var first = await h.HandleAsync(new GetOrCreateTopic(gradeId, cv, "MATH", "Mathematics", 1));
        var second = await h.HandleAsync(new GetOrCreateTopic(gradeId, cv, "MATH", "Maths (UK)", 2));

        second.Id.Should().Be(first.Id, "same CodedValueId must reuse the existing topic");
        second.Name.Should().Be("Maths (UK)", "mirrored name is updated on reuse");
        (await s.Db.Topics.CountAsync()).Should().Be(1);
    }

    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s, Guid codedValueId, int level, string name)
    {
        var gl = GradeLevel.Create(codedValueId, level, name, level);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    private static async Task SeedCurrentPeriodAsync(StudentsTestScope s, string name)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var period = Period.Create(name, today.AddDays(-1), today.AddDays(1));
        s.Db.Periods.Add(period);
        await s.Db.SaveChangesAsync();
    }
}
