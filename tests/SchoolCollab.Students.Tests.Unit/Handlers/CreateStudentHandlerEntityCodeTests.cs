using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.CQRS.Students.Commands.CreateStudent;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Tests.Unit.Handlers;

/// <summary>
/// Entity-code wiring tests for <see cref="CreateStudentHandler"/> (spec §5.2).
/// Verifies the handler invokes <see cref="IEntityCodeGenerator"/> with the
/// STUDENT_CODE rule code and assigns the generated value to
/// <c>Student.StudentNumber</c>, plus that generation failures propagate and the
/// <see cref="StudentCreated"/> integration event carries the generated number.
/// </summary>
[TestClass]
public class CreateStudentHandlerEntityCodeTests
{
    private static CreateStudentHandler NewHandler(
        StudentsTestScope s,
        Mock<IEntityCodeGenerator> generator,
        Mock<IIntegrationEventPublisher> publisher)
        => new(new StudentRepository(s.Db),
               generator.Object,
               publisher.Object,
               s.Cache,
               s.Tenants,
               NullLogger<CreateStudentHandler>.Instance);

    [TestMethod]
    public async Task HandleAsync_CallsGenerator_WithStudentCode_AndAssignsStudentNumber()
    {
        using var s = new StudentsTestScope("student-entitycode-assign");

        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync("STUDENT_CODE", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("STUA01");

        var publisher = new Mock<IIntegrationEventPublisher>();

        var handler = NewHandler(s, generator, publisher);

        var id = await handler.HandleAsync(new CreateStudent(
            StudentNumber: "LEGACY-IGNORE-ME",
            FirstName: "John",
            LastName: "Roe",
            DateOfBirth: new DateOnly(2010, 1, 1),
            GenderCodedValueId: Guid.NewGuid()));

        var student = s.Db.Students.IgnoreQueryFilters().Single(x => x.Id == id);
        student.StudentNumber.Should().Be("STUA01",
            "the generated code is the canonical StudentNumber; command.StudentNumber is ignored");

        generator.Verify(g => g.GenerateAsync("STUDENT_CODE", It.IsAny<CancellationToken>()), Times.Once);

        // The integration event carries the generated StudentNumber.
        publisher.Verify(p => p.EnqueueAsync(
            It.Is<StudentCreated>(e => e.Id == id && e.StudentNumber == "STUA01"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_GenerationFailure_PropagatesAndDoesNotPersistStudent()
    {
        using var s = new StudentsTestScope("student-entitycode-failure");

        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("no active STUDENT_CODE rule"));

        var publisher = new Mock<IIntegrationEventPublisher>();

        var handler = NewHandler(s, generator, publisher);

        var act = async () => await handler.HandleAsync(new CreateStudent(
            StudentNumber: "X",
            FirstName: "John",
            LastName: "Roe",
            DateOfBirth: new DateOnly(2010, 1, 1),
            GenderCodedValueId: Guid.NewGuid()));

        await act.Should().ThrowAsync<InvalidOperationException>();
        s.Db.Students.IgnoreQueryFilters().Should().BeEmpty(
            "a generation failure must not persist the student");
        publisher.Verify(p => p.EnqueueAsync(It.IsAny<StudentCreated>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}