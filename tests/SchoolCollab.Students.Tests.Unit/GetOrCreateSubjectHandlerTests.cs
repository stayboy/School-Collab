using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.GetOrCreateSubject;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class GetOrCreateSubjectHandlerTests
{
    private static GetOrCreateSubjectHandler NewHandler(StudentsTestScope s) =>
        new(s.Subjects, s.Cache, NullLogger<GetOrCreateSubjectHandler>.Instance);

    [TestMethod]
    public async Task GetOrCreate_CreatesWhenAbsent()
    {
        using var s = new StudentsTestScope("gocs-create");
        var cv = Guid.NewGuid();
        var h = NewHandler(s);

        var dto = await h.HandleAsync(new GetOrCreateSubject(cv, "MATH", "Mathematics", 1));

        dto.Id.Should().NotBeEmpty();
        dto.CodedValueId.Should().Be(cv);
        dto.Code.Should().Be("MATH");
        dto.Name.Should().Be("Mathematics");
        (await s.Db.Subjects.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task GetOrCreate_ReusesAndUpdatesWhenPresent()
    {
        using var s = new StudentsTestScope("gocs-reuse");
        var cv = Guid.NewGuid();
        var h = NewHandler(s);

        var first = await h.HandleAsync(new GetOrCreateSubject(cv, "MATH", "Mathematics", 1));
        var second = await h.HandleAsync(new GetOrCreateSubject(cv, "MATH", "Maths (UK)", 2));

        second.Id.Should().Be(first.Id, "same CodedValueId must reuse the existing subject");
        second.Name.Should().Be("Maths (UK)", "mirrored name is updated on reuse");
        (await s.Db.Subjects.CountAsync()).Should().Be(1);
    }
}
