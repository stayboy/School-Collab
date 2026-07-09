using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.GetOrCreateGradeLevel;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class GetOrCreateGradeLevelHandlerTests
{
    private static GetOrCreateGradeLevelHandler NewHandler(StudentsTestScope s) =>
        new(s.GradeLevels, s.Cache, s.Tenants, NullLogger<GetOrCreateGradeLevelHandler>.Instance);

    [TestMethod]
    public async Task GetOrCreate_CreatesWhenAbsent()
    {
        using var s = new StudentsTestScope("goc-create");
        var cv = Guid.NewGuid();
        var h = NewHandler(s);

        var dto = await h.HandleAsync(new GetOrCreateGradeLevel(cv, 1, "Grade 1", 2));

        dto.Id.Should().NotBeEmpty();
        dto.CodedValueId.Should().Be(cv);
        dto.Level.Should().Be(1);
        dto.Name.Should().Be("Grade 1");
        (await s.Db.GradeLevels.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task GetOrCreate_ReusesAndUpdatesWhenPresent()
    {
        using var s = new StudentsTestScope("goc-reuse");
        var cv = Guid.NewGuid();
        var h = NewHandler(s);

        var first = await h.HandleAsync(new GetOrCreateGradeLevel(cv, 1, "Grade 1", 2));
        var second = await h.HandleAsync(new GetOrCreateGradeLevel(cv, 2, "Standard 1", 3));

        second.Id.Should().Be(first.Id, "the existing grade level is reused, not duplicated");
        second.Level.Should().Be(2);
        second.Name.Should().Be("Standard 1");
        second.DisplayOrder.Should().Be(3);
        (await s.Db.GradeLevels.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task GetOrCreate_DistinctCodedValues_DistinctGradeLevels()
    {
        using var s = new StudentsTestScope("goc-distinct");
        var h = NewHandler(s);

        var a = await h.HandleAsync(new GetOrCreateGradeLevel(Guid.NewGuid(), 1, "Grade 1", 2));
        var b = await h.HandleAsync(new GetOrCreateGradeLevel(Guid.NewGuid(), 2, "Grade 2", 3));

        a.Id.Should().NotBe(b.Id);
        (await s.Db.GradeLevels.CountAsync()).Should().Be(2);
    }
}