using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.CreateTeacher;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Tests.Unit.Handlers;

/// <summary>
/// Entity-code wiring tests for <see cref="CreateTeacherHandler"/> (spec §5.3).
/// Verifies the handler invokes <see cref="IEntityCodeGenerator"/> with the
/// STAFF_CODE rule code and assigns the generated value to
/// <c>Teacher.StaffNumber</c>.
/// </summary>
[TestClass]
public class CreateTeacherHandlerEntityCodeTests
{
    [TestMethod]
    public async Task HandleAsync_CallsGenerator_WithStaffCode_AndAssignsStaffNumber()
    {
        using var s = new StudentsTestScope("teacher-entitycode-assign");

        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync("STAFF_CODE", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("STFA01");

        var handler = new CreateTeacherHandler(
            new TeacherRepository(s.Db),
            generator.Object,
            s.Cache,
            s.Tenants,
            NullLogger<CreateTeacherHandler>.Instance);

        var id = await handler.HandleAsync(
            new CreateTeacher(null, "Jane", "Doe", null));

        var teacher = s.Db.Teachers.IgnoreQueryFilters().Single(t => t.Id == id);
        teacher.StaffNumber.Should().Be("STFA01",
            "the handler must assign the generator output to Teacher.StaffNumber");

        generator.Verify(g => g.GenerateAsync("STAFF_CODE", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_GenerationFailure_PropagatesAndDoesNotPersistTeacher()
    {
        using var s = new StudentsTestScope("teacher-entitycode-failure");

        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("no active STAFF_CODE rule"));

        var handler = new CreateTeacherHandler(
            new TeacherRepository(s.Db),
            generator.Object,
            s.Cache,
            s.Tenants,
            NullLogger<CreateTeacherHandler>.Instance);

        var act = async () => await handler.HandleAsync(
            new CreateTeacher(null, "Jane", "Doe", null));

        await act.Should().ThrowAsync<InvalidOperationException>();
        s.Db.Teachers.IgnoreQueryFilters().Should().BeEmpty(
            "a generation failure must not persist the teacher");
    }
}