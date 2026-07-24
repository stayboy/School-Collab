using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.CQRS.Enrollments.Commands.EnrollStudent;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="EnrollStudentHandler"/> — the server-side command
/// handler that persists a new <see cref="StudentEnrollment"/>. This is the
/// "save" half of the enrol flow: the <c>EnrollStudentDialog</c> bUnit tests
/// cover the UI submit (POST /students/enrollments); these tests cover the
/// handler that the API endpoint delegates to.
///
/// <para>The handler owns three contracts this suite pins:</para>
/// <list type="bullet">
///   <item><b>FR-A3 active-period enforcement</b> — enrolment requires the
///         tenant's Active period. A null active period throws
///         <see cref="PeriodNotOpenException"/>; a command whose
///         <c>PeriodId</c> does not match the active period also throws
///         <see cref="PeriodNotOpenException"/>. The dialog surfaces these as
///         the "Cannot enrol students: no active period is open…" body.</item>
///   <item><b>Happy-path write</b> — when the period checks pass, the handler
///         creates the enrollment via <see cref="StudentEnrollment.Create"/>
///         (Status=Active, EnrolledOn defaults to today when null), persists it
///         via the repository, invalidates the "students" cache tag, enqueues a
///         <see cref="StudentEnrolled"/> integration event, and returns the new
///         enrollment id.</item>
///   <item><b>No duplicate-active guard at the handler</b> — two enrolments for
///         the same student+period both persist. The single-active-enrollment
///         invariant is enforced at the UX layer (the dialog's
///         <c>IsNewEnrollment</c> check + the re-enroll/transfer flow), NOT in
///         the handler. This test documents that contract so a future change to
///         add a handler-level guard is a conscious decision.</item>
/// </list>
///
/// <para>The handler's dependencies are wired with a stub
/// <see cref="IActivePeriodProvider"/> (so each test controls the active-period
/// resolution independently of the in-memory Period table), a recording
/// <see cref="IIntegrationEventPublisher"/> (so the enqueued event can be
/// asserted), the real <see cref="StudentEnrollmentRepository"/> over the
/// <see cref="StudentsTestScope"/> in-memory DbContext, and a null logger —
/// matching the established <c>GetOrCreateGradeLevelHandlerTests</c> +
/// <c>ActivePeriodProviderTests</c> patterns.</para>
/// </summary>
[TestClass]
public class EnrollStudentHandlerTests
{
    private static readonly Guid StudentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GradeLevelId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ActivePeriodId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>Stub <see cref="IActivePeriodProvider"/> with a mutable
    /// <see cref="Active"/> property so each test controls the resolution
    /// without touching the in-memory Period table.</summary>
    private sealed class StubActivePeriodProvider : IActivePeriodProvider
    {
        public ActivePeriod? Active { get; set; }
        public Task<ActivePeriod?> GetActivePeriodAsync(CancellationToken ct = default) => Task.FromResult(Active);
        public Task<ActivePeriod?> GetCurrentPeriodAsync(CancellationToken ct = default) => Task.FromResult(Active);
    }

    /// <summary>Recording publisher — captures every enqueued integration event
    /// so a test can assert both the count and the payload. Mirrors the
    /// <c>FakePublisher</c> in <c>SubmissionEngineTests</c>.</summary>
    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<object> Enqueued { get; } = new();
        public Task EnqueueAsync<T>(T message, CancellationToken ct = default) where T : class
        {
            Enqueued.Add(message);
            return Task.CompletedTask;
        }
    }

    private static ActivePeriod ActivePeriod() => new(
        ActivePeriodId, "2025/2026",
        new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31), "Active");

    private static EnrollStudentHandler NewHandler(
        StudentsTestScope s,
        StubActivePeriodProvider periods,
        RecordingPublisher publisher) =>
        new(
            new StudentEnrollmentRepository(s.Db),
            periods,
            publisher,
            s.Cache,
            NullLogger<EnrollStudentHandler>.Instance);

    // ── FR-A3: active-period enforcement ────────────────────────────────────

    [TestMethod]
    public async Task NoActivePeriod_ThrowsPeriodNotOpen_AndPersistsNothing()
    {
        using var s = new StudentsTestScope("enroll-no-active");
        var periods = new StubActivePeriodProvider { Active = null };
        var publisher = new RecordingPublisher();
        var h = NewHandler(s, periods, publisher);

        var act = () => h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, GradeLevelId, null));

        var ex = (await act.Should().ThrowAsync<PeriodNotOpenException>())
            .Which.Message;
        ex.Should().Contain("no active period is open",
            "the message surfaces the actionable tracing detail the dialog's error bar renders");
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(0,
            "no enrollment row must be persisted when the period guard rejects the command");
        publisher.Enqueued.Should().BeEmpty("no integration event must be published on a rejected enrol");
    }

    [TestMethod]
    public async Task PeriodMismatch_ThrowsPeriodNotOpen_AndPersistsNothing()
    {
        using var s = new StudentsTestScope("enroll-period-mismatch");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = NewHandler(s, periods, publisher);

        var otherPeriod = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var act = () => h.HandleAsync(new EnrollStudent(StudentId, otherPeriod, GradeLevelId, null));

        var ex = (await act.Should().ThrowAsync<PeriodNotOpenException>())
            .Which.Message;
        ex.Should().Contain("active period is").And.Contain(otherPeriod.ToString(),
            "the mismatch message names both the targeted and the active period so the tracing path is complete");
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(0,
            "no enrollment row must be persisted when the command targets a non-active period");
        publisher.Enqueued.Should().BeEmpty("no integration event must be published on a rejected enrol");
    }

    // ── Happy path ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HappyPath_PersistsEnrollment_ReturnsId_EnqueuesEvent()
    {
        using var s = new StudentsTestScope("enroll-happy");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = NewHandler(s, periods, publisher);

        var enrolledOn = new DateOnly(2025, 9, 15);
        var id = await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, GradeLevelId, enrolledOn));

        id.Should().NotBeEmpty("the handler returns the new enrollment's id");
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(1,
            "exactly one enrollment row is persisted");

        var row = await s.Db.StudentEnrollments.SingleAsync();
        row.StudentId.Should().Be(StudentId);
        row.PeriodId.Should().Be(ActivePeriodId, "the persisted period is the active one the command targeted");
        row.GradeLevelId.Should().Be(GradeLevelId);
        row.EnrolledOn.Should().Be(enrolledOn, "the provided EnrolledOn is stored verbatim");
        row.Status.Should().Be(EnrollmentStatus.Active,
            "a fresh enrolment is Active (StudentEnrollment.Create sets Status=Active)");
        row.ExitDate.Should().BeNull("a fresh enrolment has no exit date");

        // Exactly one integration event, carrying the enrollment's payload.
        publisher.Enqueued.Should().ContainSingle("exactly one StudentEnrolled event is published per enrol");
        var evt = publisher.Enqueued.OfType<StudentEnrolled>().Single();
        evt.StudentId.Should().Be(StudentId);
        evt.PeriodId.Should().Be(ActivePeriodId);
        evt.GradeLevelId.Should().Be(GradeLevelId);
        evt.EnrolledOn.Should().Be(enrolledOn);
    }

    [TestMethod]
    public async Task EnrolledOn_Null_DefaultsToToday()
    {
        // StudentEnrollment.Create defaults a null EnrolledOn to
        // DateOnly.FromDateTime(DateTime.UtcNow). The handler passes the
        // command's value straight through, so a null command value exercises
        // the domain default. Pin this so a future change to the default
        // (e.g. requiring an explicit date) is a conscious decision.
        using var s = new StudentsTestScope("enroll-default-date");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = NewHandler(s, periods, publisher);

        await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, GradeLevelId, null));

        var row = await s.Db.StudentEnrollments.SingleAsync();
        row.EnrolledOn.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow),
            "a null EnrolledOn defaults to today (UtcNow) via StudentEnrollment.Create");
    }

    // ── No duplicate-active guard at the handler ────────────────────────────

    [TestMethod]
    public async Task TwoEnrollments_ForSameStudentAndPeriod_BothPersist()
    {
        // The handler does NOT enforce a single active enrollment per
        // student+period — it creates a fresh StudentEnrollment row on every
        // call that passes the period guard. The single-active-enrollment
        // invariant is enforced at the UX layer (the dialog's
        // IsNewEnrollment check hides the inline-grade-setup path for a
        // re-enrollment; the Transfer / Withdraw flows are the supported way
        // to move an already-enrolled student). This test pins the handler's
        // current "no guard" contract so adding a handler-level duplicate
        // check later is a conscious, reviewed decision (it would change the
        // behaviour the dialog + transfer flow rely on).
        using var s = new StudentsTestScope("enroll-no-dupe-guard");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = NewHandler(s, periods, publisher);

        await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, GradeLevelId, new DateOnly(2025, 9, 15)));
        await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, GradeLevelId, new DateOnly(2025, 9, 16)));

        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(2,
            "the handler persists a second enrollment for the same student+period — no handler-level duplicate guard");
        publisher.Enqueued.OfType<StudentEnrolled>().Should().HaveCount(2,
            "one StudentEnrolled event is published per persisted enrollment");
        var rows = await s.Db.StudentEnrollments.OrderByDescending(e => e.EnrolledOn).ToArrayAsync();
        rows[0].Status.Should().Be(EnrollmentStatus.Active);
        rows[1].Status.Should().Be(EnrollmentStatus.Active,
            "both rows are Active — the handler does not auto-withdraw the prior enrollment");
    }
}