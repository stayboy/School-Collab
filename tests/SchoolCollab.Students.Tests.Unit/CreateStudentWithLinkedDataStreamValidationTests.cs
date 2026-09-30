using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Students.Commands.CreateStudentWithLinkedData;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Domain.Specifications;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// AC4 mirror for <see cref="CreateStudentWithLinkedDataHandler"/>: the atomic
/// create-with-enrollment path validates the stream against the grade↔stream bridge,
/// not the coded value's legacy <c>gradeLevel</c> attribute. Pre-fix, (i) enrolled
/// fine and (ii) threw, so both tests are red against the base commit.
/// </summary>
/// <remarks>
/// The command carries NO guardians and NO contacts — the atomic graph this suite
/// cares about is the enrollment; guardian/contact validation is covered elsewhere.
/// The scope is built by each test method (see the note in
/// <c>AssignGradeStreamHandlerTests</c> about the tenant AsyncLocal).
/// </remarks>
[TestClass]
public class CreateStudentWithLinkedDataStreamValidationTests
{
    private static readonly Guid ActivePeriodId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class StubActivePeriodProvider : IActivePeriodProvider
    {
        public ActivePeriod? Active { get; set; } = new(
            ActivePeriodId, "2025/2026",
            new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31), "Active", "AcademicYear", null);

        public Task<ActivePeriod?> GetActivePeriodAsync(CancellationToken ct = default) => Task.FromResult(Active);
        public Task<ActivePeriod?> GetCurrentPeriodAsync(CancellationToken ct = default) => Task.FromResult(Active);
        public Task<ActivePeriod?> GetActiveAcademicYearAsync(CancellationToken ct = default) => Task.FromResult(Active);
        public Task<ActivePeriod?> GetActiveSubPeriodAsync(CancellationToken ct = default) => Task.FromResult(Active);
    }

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

    private sealed class PassthroughUnitOfWork(StudentsDbContext db) : IUnitOfWork<StudentsDbContext>
    {
        public Task<TResult> ExecuteAsync<TResult>(
            Func<StudentsDbContext, CancellationToken, Task<TResult>> action,
            CancellationToken cancellationToken = default) => action(db, cancellationToken);
    }

    private sealed class StubEntityCodeGenerator : IEntityCodeGenerator
    {
        private int _seq;

        public Task<string> GenerateAsync(string ruleCode, CancellationToken ct = default)
            => Task.FromResult($"STU{Interlocked.Increment(ref _seq):D4}");

        public Task<string> GenerateWithNameAsync(string ruleCode, string? nameHint, CancellationToken ct = default)
            => Task.FromResult($"STU{Interlocked.Increment(ref _seq):D4}");
    }

    private sealed class StubFeatureFlagService : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => false;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
    }

    private static CreateStudentWithLinkedDataHandler NewHandler(StudentsTestScope s) =>
        new(new PassthroughUnitOfWork(s.Db),
            new StubEntityCodeGenerator(),
            s.Db,
            s.Cache,
            s.Tenants,
            new StubActivePeriodProvider(),
            s.GradeLevels,
            s.GradeStreamAssignments,
            new StubCodedValuesApi(),
            new StubFeatureFlagService(),
            new CompositeEnrollmentSpecification(new ILeafEnrollmentSpecification[]
            {
                new AgeRangeSpecification(),
                new GenderRestrictionSpecification(),
                new SingleActiveEnrollmentSpecification(),
            }),
            new RecordingPublisher(),
            NullLogger<CreateStudentWithLinkedDataHandler>.Instance);

    private static async Task<Guid> SeedGradeAsync(StudentsTestScope scope)
    {
        var grade = GradeLevel.Create(Guid.NewGuid(), level: 5, name: "Grade 5", displayOrder: 5);
        scope.Db.GradeLevels.Add(grade);
        await scope.Db.SaveChangesAsync();
        return grade.Id;
    }

    private static CreateStudentWithLinkedData Command(Guid gradeLevelId, Guid? streamId) =>
        new("Test", "Student", new DateOnly(2012, 1, 15), Guid.NewGuid(),
            EnrollmentGradeLevelId: gradeLevelId,
            StreamCodedValueId: streamId);

    [TestMethod]
    public async Task StreamNotOfferedByTheGrade_Throws_EvenWhenTheAttributeMatches()
    {
        using var s = new StudentsTestScope("cswld-stream-no-bridge");
        var gradeLevelId = await SeedGradeAsync(s);
        var streamId = Guid.NewGuid();

        var act = () => NewHandler(s).HandleAsync(Command(gradeLevelId, streamId));

        await act.Should().ThrowAsync<StreamGradeMismatchException>();
        (await s.Db.Students.CountAsync()).Should().Be(0,
            "the enrollment-target validation runs BEFORE the unit of work, so nothing is written");
    }

    [TestMethod]
    public async Task StreamOfferedByTheGrade_CreatesStudentAndEnrollment()
    {
        using var s = new StudentsTestScope("cswld-stream-bridge");
        var gradeLevelId = await SeedGradeAsync(s);
        var streamId = Guid.NewGuid();
        await s.GradeStreamAssignments.AddOrReuseAsync(GradeStreamAssignment.Create(gradeLevelId, streamId));

        var studentId = await NewHandler(s).HandleAsync(Command(gradeLevelId, streamId));

        studentId.Should().NotBeEmpty();
        var enrollment = s.Db.StudentEnrollments.Single();
        enrollment.GradeLevelId.Should().Be(gradeLevelId);
        enrollment.StreamCodedValueId.Should().Be(streamId);
    }
}
