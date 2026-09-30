using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Enrollments.Commands.TransferStudent;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// AC4 mirror for <see cref="TransferStudentHandler"/>: stream validation reads the
/// bridge, not the coded value's legacy <c>gradeLevel</c> attribute. Pre-fix the
/// handler string-parsed that attribute over an HTTP hop, so both tests here are red
/// against the base commit (the first enrolled fine, the second threw).
/// </summary>
/// <remarks>
/// The scope is built by each test method (see the note in
/// <c>AssignGradeStreamHandlerTests</c> about the tenant AsyncLocal).
/// </remarks>
[TestClass]
public class TransferStudentHandlerStreamValidationTests
{
    private static readonly Guid ActivePeriodId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<object> Enqueued { get; } = [];

        public Task EnqueueAsync<T>(T message, CancellationToken ct = default) where T : class
        {
            Enqueued.Add(message);
            return Task.CompletedTask;
        }

        public Task EnqueueAsync<T>(T message, Guid? tenantStamp, CancellationToken ct = default) where T : class
            => EnqueueAsync(message, ct);
    }

    private static TransferStudentHandler NewHandler(StudentsTestScope s) =>
        new(new StudentEnrollmentRepository(s.Db),
            s.GradeLevels,
            s.GradeStreamAssignments,
            s.Db,
            new SystemActorAccessor("test:actor", "Test Actor"),
            new RecordingPublisher(),
            s.Cache,
            NullLogger<TransferStudentHandler>.Instance);

    private static async Task<(Guid GradeLevelId, Guid EnrollmentId)> SeedAsync(StudentsTestScope s)
    {
        var grade = GradeLevel.Create(Guid.NewGuid(), level: 5, name: "Grade 5", displayOrder: 5);
        s.Db.GradeLevels.Add(grade);

        var student = Student.Create("STU-TRANSFER", "Test", "Student",
            new DateOnly(2012, 1, 15), Guid.NewGuid());
        s.Db.Students.Add(student);

        var enrollment = StudentEnrollment.Create(student.Id, ActivePeriodId, grade.Id);
        s.Db.StudentEnrollments.Add(enrollment);
        await s.Db.SaveChangesAsync();

        return (grade.Id, enrollment.Id);
    }

    [TestMethod]
    public async Task StreamNotOfferedByTheTargetGrade_Throws_EvenWhenTheAttributeMatches()
    {
        // AC4(i) mirror: with no bridge row the target grade does not offer the
        // stream, so the transfer is rejected — even though the coded value's legacy
        // `gradeLevel` attribute referenced exactly this grade (the pre-fix pass).
        using var s = new StudentsTestScope("transfer-stream-no-bridge");
        var (gradeLevelId, enrollmentId) = await SeedAsync(s);
        var streamId = Guid.NewGuid();

        var act = () => NewHandler(s).HandleAsync(
            new TransferStudent(enrollmentId, gradeLevelId, streamId, null, "Move to 5A"));

        (await act.Should().ThrowAsync<StreamGradeMismatchException>())
            .Which.StreamCodedValueId.Should().Be(streamId);
    }

    [TestMethod]
    public async Task StreamOfferedByTheTargetGrade_Transfers_Successfully()
    {
        // AC4(ii) mirror: the bridge row is the ONLY thing that matters — the coded
        // value carries no `gradeLevel` attribute at all (the state of every stream
        // created through the new dialog).
        using var s = new StudentsTestScope("transfer-stream-bridge");
        var (gradeLevelId, enrollmentId) = await SeedAsync(s);
        var streamId = Guid.NewGuid();
        await s.GradeStreamAssignments.AddOrReuseAsync(GradeStreamAssignment.Create(gradeLevelId, streamId));

        await NewHandler(s).HandleAsync(
            new TransferStudent(enrollmentId, gradeLevelId, streamId, null, "Move to 5A"));

        var row = await s.Db.StudentEnrollments.FindAsync(enrollmentId);
        row!.StreamCodedValueId.Should().Be(streamId);
    }

    [TestMethod]
    public async Task NullStream_ClearsTheStream_WithoutAnyStreamValidation()
    {
        using var s = new StudentsTestScope("transfer-stream-cleared");
        var (gradeLevelId, enrollmentId) = await SeedAsync(s);

        await NewHandler(s).HandleAsync(
            new TransferStudent(enrollmentId, gradeLevelId, null, null, "Grade move only"));

        var row = await s.Db.StudentEnrollments.FindAsync(enrollmentId);
        row!.StreamCodedValueId.Should().BeNull();
    }
}
