using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.SetRequiresEnrollment;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// The <c>RequiresEnrollment</c> switch (subject-enrollment-requirement-flag.md §4).
/// <para>Acceptance criterion 10: <c>false → true</c> grandfathers the whole audience — the flag
/// and every materialised <see cref="StudentTopicAssignment"/> ride <b>one</b> <c>SaveChanges</c>
/// inside the unit of work, and a save that fails leaves neither the flag nor a partial row set
/// (FR-ER-7/10).</para>
/// <para>Acceptance criterion 11: <c>true → false</c> keeps the rows (FR-ER-9) and the response
/// reports the audience / enrolled / un-enrolled counts (FR-ER-8).</para>
/// </summary>
[TestClass]
public class SetRequiresEnrollmentHandlerTests
{
    private static readonly Guid ActiveYearId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GradeLevelId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid SubPeriodId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>The active academic year resolves; the active sub-period does not (the gap state).</summary>
    private sealed class StubActivePeriodProvider(ActivePeriod? activeYear) : IActivePeriodProvider
    {
        public Task<ActivePeriod?> GetActivePeriodAsync(CancellationToken ct = default) => Task.FromResult(activeYear);
        public Task<ActivePeriod?> GetCurrentPeriodAsync(CancellationToken ct = default) => Task.FromResult(activeYear);
        public Task<ActivePeriod?> GetActiveAcademicYearAsync(CancellationToken ct = default) => Task.FromResult(activeYear);
        public Task<ActivePeriod?> GetActiveSubPeriodAsync(CancellationToken ct = default) => Task.FromResult<ActivePeriod?>(null);
    }

    private static ActivePeriod ActiveYear() => new(
        ActiveYearId, "2026/2027", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31),
        "Active", "AcademicYear", null);

    private sealed class PassthroughUnitOfWork(StudentsDbContext db) : IUnitOfWork<StudentsDbContext>
    {
        public Task<TResult> ExecuteAsync<TResult>(
            Func<StudentsDbContext, CancellationToken, Task<TResult>> action,
            CancellationToken cancellationToken = default) => action(db, cancellationToken);
    }

    /// <summary>
    /// Counts <c>SaveChanges</c> calls and how many <see cref="StudentTopicAssignment"/> rows each
    /// one carried; can fail the save instead of committing it.
    /// </summary>
    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Calls { get; private set; }
        public List<int> AddedStudentTopicAssignmentsPerCall { get; } = [];
        public bool FailOnSave { get; set; }

        /// <summary>Forgets the setup writes, so the assertions see only the handler's own save.</summary>
        public void Reset()
        {
            Calls = 0;
            AddedStudentTopicAssignmentsPerCall.Clear();
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            AddedStudentTopicAssignmentsPerCall.Add(
                eventData.Context!.ChangeTracker.Entries<StudentTopicAssignment>()
                    .Count(e => e.State == EntityState.Added));

            if (FailOnSave) throw new InvalidOperationException("test-synthetic save failure");

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static SetRequiresEnrollmentHandler NewHandler(StudentsTestScope s, ActivePeriod? activeYear) =>
        new(s.Db,
            new PassthroughUnitOfWork(s.Db),
            new StubActivePeriodProvider(activeYear),
            s.Cache,
            NullLogger<SetRequiresEnrollmentHandler>.Instance);

    private static GradeTopicAssignment SeedGradeSubject(StudentsTestScope s, bool requiresEnrollment)
    {
        var bridge = GradeTopicAssignment.Create(GradeLevelId, Guid.NewGuid(), Today());
        if (requiresEnrollment) bridge.SetRequiresEnrollment(true);
        s.Db.GradeTopicAssignments.Add(bridge);
        return bridge;
    }

    private static Guid SeedStudentInGrade(StudentsTestScope s, string number)
    {
        var student = Student.Create(number, number, "Pupil", new DateOnly(2015, 1, 1), Guid.NewGuid());
        s.Db.Students.Add(student);
        s.Db.StudentEnrollments.Add(StudentEnrollment.Create(student.Id, ActiveYearId, GradeLevelId));
        return student.Id;
    }

    private static void SeedEnrollmentRow(StudentsTestScope s, Guid studentId, Guid topicId, Guid periodId) =>
        s.Db.StudentTopicAssignments.Add(StudentTopicAssignment.Create(
            studentId, topicId, periodId, isOverride: false, SubjectAssignmentSource.GradeAssignment));

    [TestMethod]
    public async Task FlippingToTrue_GrandfathersTheWholeAudience_InOneSaveChanges()
    {
        var counter = new SaveCounter();
        using var s = new StudentsTestScope("set-requires-enrollment-grandfather", counter);
        var bridge = SeedGradeSubject(s, requiresEnrollment: false);
        var alreadyEnrolled = SeedStudentInGrade(s, "S1");
        SeedStudentInGrade(s, "S2");
        SeedStudentInGrade(s, "S3");
        SeedEnrollmentRow(s, alreadyEnrolled, bridge.TopicId, ActiveYearId);
        await s.Db.SaveChangesAsync();
        counter.Reset();

        var counts = await NewHandler(s, ActiveYear())
            .HandleAsync(new SetRequiresEnrollment(bridge.Id, Value: true));

        counts.AudienceCount.Should().Be(3);
        counts.EnrolledCount.Should().Be(1, "the counts describe the audience state the switch is changing");
        counts.UnenrolledCount.Should().Be(2);
        counts.RequiresEnrollment.Should().BeTrue();

        counter.Calls.Should().Be(1, "the flag and every grandfather row ride a single SaveChanges (FR-ER-10)");
        counter.AddedStudentTopicAssignmentsPerCall.Single().Should().Be(2);

        var rows = await s.Db.StudentTopicAssignments
            .Where(x => x.TopicId == bridge.TopicId && x.PeriodId == ActiveYearId)
            .ToArrayAsync();
        rows.Should().HaveCount(3, "every current audience member ends up with a row — one was already there");
        rows.Should().OnlyContain(x => x.SourceType == SubjectAssignmentSource.GradeAssignment);
        rows.Should().OnlyContain(x => x.SubPeriodId == null, "the gap state stamps no active sub-period");

        (await s.Db.GradeTopicAssignments.SingleAsync(x => x.Id == bridge.Id))
            .RequiresEnrollment.Should().BeTrue();
    }

    [TestMethod]
    public async Task FlippingToTrue_IsIdempotent_AndDoesNotDuplicateRows()
    {
        using var s = new StudentsTestScope("set-requires-enrollment-idempotent");
        var bridge = SeedGradeSubject(s, requiresEnrollment: false);
        SeedStudentInGrade(s, "S1");
        SeedStudentInGrade(s, "S2");
        await s.Db.SaveChangesAsync();

        await NewHandler(s, ActiveYear()).HandleAsync(new SetRequiresEnrollment(bridge.Id, Value: true));
        var second = await NewHandler(s, ActiveYear()).HandleAsync(new SetRequiresEnrollment(bridge.Id, Value: true));

        second.UnenrolledCount.Should().Be(0, "the first flip enrolled everyone");
        (await s.Db.StudentTopicAssignments.CountAsync()).Should().Be(2,
            "a student who already holds a row for the year is not re-inserted — the unique index would abort");
    }

    [TestMethod]
    public async Task RedundantTrueFlip_DoesNotEnrolNewlyAddedGradeMembers()
    {
        var counter = new SaveCounter();
        using var s = new StudentsTestScope("set-requires-enrollment-redundant-true", counter);
        var bridge = SeedGradeSubject(s, requiresEnrollment: true);
        var alreadyEnrolled = SeedStudentInGrade(s, "S1");
        SeedEnrollmentRow(s, alreadyEnrolled, bridge.TopicId, ActiveYearId);
        await s.Db.SaveChangesAsync();

        // A grade member added after the flag was already true holds no row: under explicit-enrolment
        // semantics they are enrolled by hand, never by implication.
        var newcomer = SeedStudentInGrade(s, "S2");
        await s.Db.SaveChangesAsync();
        counter.Reset();

        var counts = await NewHandler(s, ActiveYear())
            .HandleAsync(new SetRequiresEnrollment(bridge.Id, Value: true));

        counts.AudienceCount.Should().Be(2);
        counts.EnrolledCount.Should().Be(1);
        counts.UnenrolledCount.Should().Be(1, "the counts describe the pre-mutation state in both directions");
        counts.RequiresEnrollment.Should().BeTrue();

        counter.AddedStudentTopicAssignmentsPerCall.Sum().Should().Be(0,
            "only a false → true flip grandfathers (FR-ER-7); a redundant true → true flip inserts nothing");

        (await s.Db.StudentTopicAssignments.AnyAsync(
                x => x.StudentId == newcomer && x.TopicId == bridge.TopicId && x.PeriodId == ActiveYearId))
            .Should().BeFalse("a redundant flip must not silently enrol a student who has no row");
        (await s.Db.StudentTopicAssignments.CountAsync(
                x => x.TopicId == bridge.TopicId && x.PeriodId == ActiveYearId))
            .Should().Be(1, "the pre-existing row is untouched and nothing was added");
    }

    [TestMethod]
    public async Task FlippingToTrue_WhenTheSaveFails_LeavesNeitherTheFlagNorAnyRow()
    {
        var counter = new SaveCounter();
        using var s = new StudentsTestScope("set-requires-enrollment-failure", counter);
        var bridge = SeedGradeSubject(s, requiresEnrollment: false);
        SeedStudentInGrade(s, "S1");
        SeedStudentInGrade(s, "S2");
        await s.Db.SaveChangesAsync();
        counter.Reset();
        counter.FailOnSave = true;
        var bridgeId = bridge.Id;
        var topicId = bridge.TopicId;

        var act = () => NewHandler(s, ActiveYear())
            .HandleAsync(new SetRequiresEnrollment(bridgeId, Value: true));

        await act.Should().ThrowAsync<InvalidOperationException>();
        counter.Calls.Should().Be(1, "the handler makes exactly one save, so its failure is the whole write's failure");

        // Read through a FRESH context: the failing context still tracks the unsaved changes.
        await using var fresh = s.NewContext();
        (await fresh.GradeTopicAssignments.SingleAsync(x => x.Id == bridgeId))
            .RequiresEnrollment.Should().BeFalse("a partial grandfather is worse than none (FR-ER-10)");
        (await fresh.StudentTopicAssignments.CountAsync(x => x.TopicId == topicId))
            .Should().Be(0, "no student row survived the failed write");
    }

    [TestMethod]
    public async Task FlippingToFalse_KeepsTheRows_AndReportsTheCounts()
    {
        var counter = new SaveCounter();
        using var s = new StudentsTestScope("set-requires-enrollment-inert-history", counter);
        var bridge = SeedGradeSubject(s, requiresEnrollment: true);
        var first = SeedStudentInGrade(s, "S1");
        var second = SeedStudentInGrade(s, "S2");
        SeedStudentInGrade(s, "S3");
        SeedEnrollmentRow(s, first, bridge.TopicId, ActiveYearId);
        SeedEnrollmentRow(s, second, bridge.TopicId, ActiveYearId);
        await s.Db.SaveChangesAsync();
        counter.Reset();

        var counts = await NewHandler(s, ActiveYear())
            .HandleAsync(new SetRequiresEnrollment(bridge.Id, Value: false));

        counts.AudienceCount.Should().Be(3);
        counts.EnrolledCount.Should().Be(2);
        counts.UnenrolledCount.Should().Be(1);
        counts.RequiresEnrollment.Should().BeFalse();

        counter.AddedStudentTopicAssignmentsPerCall.Single().Should().Be(0,
            "flipping back adds nothing");
        (await s.Db.StudentTopicAssignments.CountAsync(x => x.TopicId == bridge.TopicId && x.PeriodId == ActiveYearId))
            .Should().Be(2, "the rows stay as inert history (FR-ER-9)");
    }

    [TestMethod]
    public async Task NoActiveAcademicYear_ThrowsPeriodNotOpen()
    {
        using var s = new StudentsTestScope("set-requires-enrollment-no-year");
        var bridge = SeedGradeSubject(s, requiresEnrollment: false);
        SeedStudentInGrade(s, "S1");
        await s.Db.SaveChangesAsync();

        var act = () => NewHandler(s, activeYear: null)
            .HandleAsync(new SetRequiresEnrollment(bridge.Id, Value: true));

        await act.Should().ThrowAsync<PeriodNotOpenException>(
            "enrolment rows are year-keyed (FR-ER-3), so there is no year to grandfather into");
    }

    [TestMethod]
    public async Task UnknownOrActivityGroupAssignmentId_ThrowsKeyNotFound()
    {
        using var s = new StudentsTestScope("set-requires-enrollment-unknown");
        var group = ActivityGroupTopicAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Today());
        group.SetRequiresEnrollment(false);
        s.Db.ActivityGroupTopicAssignments.Add(group);
        await s.Db.SaveChangesAsync();

        var act = () => NewHandler(s, ActiveYear())
            .HandleAsync(new SetRequiresEnrollment(group.Id, Value: true));

        await act.Should().ThrowAsync<KeyNotFoundException>(
            "the flag's audience and grandfather exist for a grade; a group id is not a switch target in this layer");
    }
}
