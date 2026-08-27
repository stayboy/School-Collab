using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Specifications;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.CQRS.Enrollments.Commands.EnrollStudent;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.Services;

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
///   <item><b>Upsert on re-enroll</b> — submitting the enroll command for a
///         student already actively enrolled in the target period updates that
///         row in place (same id returned): a no-op when grade+stream match,
///         an audited grade/stream correction otherwise. The unique index
///         <c>ix_student_enrollments_tenant_student_period</c> makes any other
///         behaviour impossible (23505), and the insert itself is race-safe via
///         <see cref="StudentEnrollmentRepository.AddOrReuseAsync"/>.</item>
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
    private static readonly Guid ActivePeriodId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GenderMale = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid GenderFemale = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    /// <summary>Stub <see cref="IActivePeriodProvider"/> with a mutable
    /// <see cref="Active"/> property so each test controls the resolution
    /// without touching the in-memory Period table.</summary>
    private sealed class StubActivePeriodProvider : IActivePeriodProvider
    {
        public ActivePeriod? Active { get; set; }
        public Task<ActivePeriod?> GetActivePeriodAsync(CancellationToken ct = default) => Task.FromResult(Active);
        public Task<ActivePeriod?> GetCurrentPeriodAsync(CancellationToken ct = default) => Task.FromResult(Active);
        public Task<ActivePeriod?> GetActiveAcademicYearAsync(CancellationToken ct = default) => Task.FromResult(Active);
        public Task<ActivePeriod?> GetActiveSubPeriodAsync(CancellationToken ct = default) => Task.FromResult(Active);
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

        public Task EnqueueAsync<T>(T message, Guid? tenantStamp, CancellationToken ct = default)
            where T : class
            => EnqueueAsync(message, ct);
    }

    /// <summary>Stub <see cref="IFeatureFlagService"/> with a fixed verdict so each
    /// handler test controls whether the enrollment-validation guard runs. Default
    /// (<c>false</c>) preserves the pre-flag behaviour the existing tests pin.</summary>
    private sealed class StubFeatureFlagService(bool enabled) : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => enabled;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
    }

    private static ActivePeriod ActivePeriod() => new(
        ActivePeriodId, "2025/2026",
        new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31), "Active", "AcademicYear", null);

    private static async Task<EnrollStudentHandler> NewHandler(
        StudentsTestScope s,
        StubActivePeriodProvider periods,
        RecordingPublisher publisher,
        bool flagEnabled = false,
        int? minAge = null,
        int? maxAge = null,
        Guid? allowedGender = null)
    {
        // Seed a GradeLevel for stream validation. The handler resolves the
        // enrollment's GradeLevelId → GradeLevel → CodedValueId, and the
        // StubCodedValuesApiClient returns a stream whose gradeLevel
        // attribute matches this CodedValueId. We use the factory-generated
        // Id (read back from the seeded entity) so EF InMemory doesn't
        // complain about modifying a primary key.
        var gradeCodedValueId = Guid.Parse("22222222-2222-2222-2222-222222222223");
        var gradeLevel = GradeLevel.Create(
            gradeCodedValueId,
            level: 1,
            name: "Grade 1",
            displayOrder: 1,
            minAge,
            maxAge,
            allowedGender);
        s.Db.GradeLevels.Add(gradeLevel);
        s.Db.SaveChanges();
        // The stub stream references this specific CodedValueId; the handler
        // resolves enrollment.GradeLevelId → gradeLevel.CodedValueId and
        // compares. The InMemoryGradeLevelRepository reads from the same
        // DbContext, so gradeLevel.Id is discoverable via the seeded row.

        return new EnrollStudentHandler(
            new StudentEnrollmentRepository(s.Db),
            periods,
            new InMemoryGradeLevelRepository(s.Db),
            new StubCodedValuesApiClient(),
            publisher,
            s.Cache,
            NullLogger<EnrollStudentHandler>.Instance,
            new StubFeatureFlagService(flagEnabled),
            new StudentRepository(s.Db),
            new CompositeEnrollmentSpecification(new ILeafEnrollmentSpecification[]
            {
                new AgeRangeSpecification(),
                new GenderRestrictionSpecification(),
                new SingleActiveEnrollmentSpecification()
            }),
            s.Tenants,
            s.Db,
            TestActor);
    }

    /// <summary>Fixed audit actor for handler tests (the upsert writes a
    /// StudentTransferAuditEntry through the shared auditor).</summary>
    private static readonly SystemActorAccessor TestActor = new("test:actor", "Test Actor");

    // ── FR-A3: active-period enforcement ────────────────────────────────────

    [TestMethod]
    public async Task NoActivePeriod_ThrowsPeriodNotOpen_AndPersistsNothing()
    {
        using var s = new StudentsTestScope("enroll-no-active");
        var periods = new StubActivePeriodProvider { Active = null };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher);

        var gradeLevel = s.Db.GradeLevels.Single();
        var act = () => h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, gradeLevel.CodedValueId, null, null));

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
        var h = await NewHandler(s, periods, publisher);

        var otherPeriod = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var gradeLevel = s.Db.GradeLevels.Single();
        var act = () => h.HandleAsync(new EnrollStudent(StudentId, otherPeriod, gradeLevel.CodedValueId, null, null));

        var ex = (await act.Should().ThrowAsync<PeriodNotOpenException>())
            .Which.Message;
        ex.Should().Contain("active period is").And.Contain(otherPeriod.ToString(),
            "the mismatch message names both the targeted and the active period so the tracing path is complete");
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(0,
            "no enrollment row must be persisted when the command targets a non-active period");
        publisher.Enqueued.Should().BeEmpty("no integration event must be published on a rejected enrol");
    }

    [TestMethod]
    public async Task ActiveSubPeriod_ThrowsYearLevelPeriodNotOpen_AndPersistsNothing()
    {
        // FR-H9 / AC-H8: grade enrollment is year-level. Even if the active period
        // resolved to a Term/Semester sub-period, enrollment must be rejected.
        using var s = new StudentsTestScope("enroll-sub-period-active");
        var periods = new StubActivePeriodProvider
        {
            Active = new ActivePeriod(
                ActivePeriodId, "T1",
                new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), "Active", "Term", null)
        };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher);

        var gradeLevel = s.Db.GradeLevels.Single();
        var act = () => h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, gradeLevel.CodedValueId, null, null));

        var ex = (await act.Should().ThrowAsync<PeriodNotOpenException>()).Which.Message;
        ex.Should().Contain("year-level")
            .And.Contain("Term", "the message names the sub-period type it rejects");
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(0);
        publisher.Enqueued.Should().BeEmpty();
    }

    // ── Happy path ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HappyPath_PersistsEnrollment_ReturnsId_EnqueuesEvent()
    {
        using var s = new StudentsTestScope("enroll-happy");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher);

        var enrolledOn = new DateOnly(2025, 9, 15);
        var gradeLevel = s.Db.GradeLevels.Single();
        var id = await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, gradeLevel.CodedValueId, null, enrolledOn));

        id.Should().NotBeEmpty("the handler returns the new enrollment's id");
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(1,
            "exactly one enrollment row is persisted");

        var row = await s.Db.StudentEnrollments.SingleAsync();
        row.StudentId.Should().Be(StudentId);
        row.PeriodId.Should().Be(ActivePeriodId, "the persisted period is the active one the command targeted");
        row.GradeLevelId.Should().Be(gradeLevel.Id);
        row.EnrolledOn.Should().Be(enrolledOn, "the provided EnrolledOn is stored verbatim");
        row.Status.Should().Be(EnrollmentStatus.Active,
            "a fresh enrolment is Active (StudentEnrollment.Create sets Status=Active)");
        row.ExitDate.Should().BeNull("a fresh enrolment has no exit date");

        // Exactly one integration event, carrying the enrollment's payload.
        publisher.Enqueued.Should().ContainSingle("exactly one StudentEnrolled event is published per enrol");
        var evt = publisher.Enqueued.OfType<StudentEnrolled>().Single();
        evt.StudentId.Should().Be(StudentId);
        evt.PeriodId.Should().Be(ActivePeriodId);
        evt.GradeLevelId.Should().Be(gradeLevel.Id);
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
        var h = await NewHandler(s, periods, publisher);

        var gradeLevel = s.Db.GradeLevels.Single();
        await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, gradeLevel.CodedValueId, null, null));

        var row = await s.Db.StudentEnrollments.SingleAsync();
        row.EnrolledOn.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow),
            "a null EnrolledOn defaults to today (UtcNow) via StudentEnrollment.Create");
    }

    // ── Upsert: re-enrolling an already-enrolled student ─────────────────────

    [TestMethod]
    public async Task ReEnroll_SameGradeAndStream_IsNoOp_ReturnsExistingId()
    {
        // The Enroll-dialog upsert: submitting the enroll command again for a
        // student who is already actively enrolled in the active period with the
        // SAME grade+stream must be an idempotent no-op — it returns the existing
        // enrollment id, persists no second row, publishes no extra events, and
        // leaves UpdatedAt untouched. This replaces the former "two rows persist"
        // contract, which collided with ix_student_enrollments_tenant_student_period.
        using var s = new StudentsTestScope("enroll-upsert-noop");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher);

        var gradeLevel = s.Db.GradeLevels.Single();
        var firstId = await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, gradeLevel.CodedValueId, null, new DateOnly(2025, 9, 15)));
        var updatedAtBefore = (await s.Db.StudentEnrollments.SingleAsync()).UpdatedAt;

        var secondId = await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, gradeLevel.CodedValueId, null, new DateOnly(2025, 9, 16)));

        secondId.Should().Be(firstId, "the no-op returns the existing enrollment id");
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(1,
            "the unique index (tenant, student, period) forbids a second row — the upsert converges instead");
        (await s.Db.StudentEnrollments.SingleAsync()).UpdatedAt.Should().Be(updatedAtBefore,
            "a no-op must not touch the enrollment row");
        publisher.Enqueued.OfType<StudentEnrolled>().Should().ContainSingle("only the original insert publishes StudentEnrolled");
        publisher.Enqueued.OfType<StudentEnrollmentUpdated>().Should().BeEmpty("a no-op publishes no update event");
        s.Db.StudentTransferAuditEntries.ToList().Should().BeEmpty("a no-op writes no audit entry");
    }

    [TestMethod]
    public async Task ReEnroll_DifferentGrade_UpdatesInPlace_StaysActive_Audits_PublishesUpdatedEvent()
    {
        using var s = new StudentsTestScope("enroll-upsert-update");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher);

        var oldGrade = s.Db.GradeLevels.First();
        var firstId = await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, oldGrade.CodedValueId, null, new DateOnly(2025, 9, 15)));

        // A second grade level so the resubmit targets a different grade.
        var newCodedValueId = Guid.Parse("22222222-2222-2222-2222-222222222224");
        s.Db.GradeLevels.Add(GradeLevel.Create(newCodedValueId, level: 2, name: "Grade 2", displayOrder: 2));
        s.Db.SaveChanges();

        var secondId = await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, newCodedValueId, null, null));

        secondId.Should().Be(firstId, "the update path keeps the SAME enrollment row (upsert)");
        var row = await s.Db.StudentEnrollments.SingleAsync();
        row.Id.Should().Be(firstId);
        var updatedGradeLevel = s.Db.GradeLevels.Single(g => g.CodedValueId == newCodedValueId);
        row.GradeLevelId.Should().Be(updatedGradeLevel.Id, "the grade was corrected in place");
        row.Status.Should().Be(EnrollmentStatus.Active,
            "UpdateGrade corrects the row without flipping Status to Transferred");
        row.ExitDate.Should().BeNull("an in-place correction stamps no exit date");

        publisher.Enqueued.OfType<StudentEnrolled>().Should().ContainSingle("only the original insert publishes StudentEnrolled");
        var upd = publisher.Enqueued.OfType<StudentEnrollmentUpdated>().Should().ContainSingle().Which;
        upd.StudentId.Should().Be(StudentId);
        upd.PeriodId.Should().Be(ActivePeriodId);
        upd.PreviousGradeLevelId.Should().Be(oldGrade.Id);
        upd.NewGradeLevelId.Should().Be(updatedGradeLevel.Id);

        // Audit trail: exactly one grade-change entry covering previous → new.
        var audit = s.Db.StudentTransferAuditEntries.Should().ContainSingle().Which;
        audit.FromGradeLevelId.Should().Be(oldGrade.Id);
        audit.ToGradeLevelId.Should().Be(updatedGradeLevel.Id);
        audit.ActorId.Should().Be("test:actor");
    }

    [TestMethod]
    public async Task ReEnroll_SameGrade_DifferentStream_UpdatesStreamOnly()
    {
        // The upsert equality check compares BOTH GradeLevelId and
        // StreamCodedValueId. A change that only touches the stream must still
        // trigger UpdateGrade (same id, same grade, new stream) and publish an
        // update event — this prevents a silent no-op when the user switches
        // from "no stream" to a stream for the same grade.
        using var s = new StudentsTestScope("enroll-upsert-stream-only");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher);

        var gradeLevel = s.Db.GradeLevels.Single();
        var firstId = await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, gradeLevel.CodedValueId, null, null));

        var streamId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var secondId = await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, gradeLevel.CodedValueId, streamId, null));

        secondId.Should().Be(firstId);
        var row = await s.Db.StudentEnrollments.SingleAsync();
        row.GradeLevelId.Should().Be(gradeLevel.Id, "the grade must not change in a stream-only update");
        row.StreamCodedValueId.Should().Be(streamId, "the stream must be updated in place");
        row.Status.Should().Be(EnrollmentStatus.Active);

        var upd = publisher.Enqueued.OfType<StudentEnrollmentUpdated>().Should().ContainSingle().Which;
        upd.PreviousGradeLevelId.Should().Be(gradeLevel.Id);
        upd.NewGradeLevelId.Should().Be(gradeLevel.Id);
        upd.NewStreamCodedValueId.Should().Be(streamId);

        var audit = s.Db.StudentTransferAuditEntries.Should().ContainSingle().Which;
        audit.FromGradeLevelId.Should().Be(gradeLevel.Id);
        audit.ToGradeLevelId.Should().Be(gradeLevel.Id,
            "the grade-level audit entry records no grade change for a stream-only update");
    }

    [TestMethod]
    public async Task ReEnroll_InsertRace_ConvergesOnWinningRow()
    {
        // Same shape as ConcurrentMaterialization_LosesRace_EnrollsAgainstWinningRow:
        // the lookup sees no row but the INSERT loses the (tenant, student, period)
        // unique-index race. The handler must converge on the winner's row (update
        // its grade/stream) rather than fail with a raw DbUpdateException.
        using var s = new StudentsTestScope("enroll-upsert-race");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();

        var gradeLevel = GradeLevel.Create(
            Guid.Parse("22222222-2222-2222-2222-222222222223"), level: 1, name: "Grade 1", displayOrder: 1);
        s.Db.GradeLevels.Add(gradeLevel);
        s.Db.SaveChanges();

        // The winner row the concurrent request committed between our lookup and
        // our insert — enrolled in a DIFFERENT grade so convergence must update it.
        var winnerCodedValueId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var winnerGrade = GradeLevel.Create(winnerCodedValueId, level: 3, name: "Winner Grade", displayOrder: 3);
        s.Db.GradeLevels.Add(winnerGrade);
        s.Db.SaveChanges();
        var winner = StudentEnrollment.Create(StudentId, ActivePeriodId, winnerGrade.Id);
        s.Db.StudentEnrollments.Add(winner);
        s.Db.SaveChanges();

        var racingRepo = new RacingEnrollmentRepository(
            new StudentEnrollmentRepository(s.Db),
            invisibleUntilConflict: () => s.Db.StudentEnrollments.FirstOrDefault(
                e => e.StudentId == StudentId && e.PeriodId == ActivePeriodId && e.Status == EnrollmentStatus.Active));

        var h = new EnrollStudentHandler(
            racingRepo,
            periods,
            new InMemoryGradeLevelRepository(s.Db),
            new StubCodedValuesApiClient(),
            publisher,
            s.Cache,
            NullLogger<EnrollStudentHandler>.Instance,
            new StubFeatureFlagService(false),
            new StudentRepository(s.Db),
            new CompositeEnrollmentSpecification(new ILeafEnrollmentSpecification[]
            {
                new AgeRangeSpecification(),
                new GenderRestrictionSpecification(),
                new SingleActiveEnrollmentSpecification()
            }),
            s.Tenants,
            s.Db,
            TestActor);

        var id = await h.HandleAsync(new EnrollStudent(StudentId, ActivePeriodId, gradeLevel.CodedValueId, null, null));

        id.Should().NotBeEmpty("the command succeeds despite losing the insert race");
        racingRepo.ConflictCount.Should().Be(1, "the insert went through AddOrReuseAsync and simulated the 23505");
        (await s.Db.StudentEnrollments.CountAsync(e => e.StudentId == StudentId && e.PeriodId == ActivePeriodId)).Should().Be(1,
            "exactly one enrollment row per (tenant, student, period) survives the race");
        var row = await s.Db.StudentEnrollments.SingleAsync(e => e.StudentId == StudentId && e.PeriodId == ActivePeriodId);
        row.Id.Should().Be(winner.Id, "the winning row is kept");
        row.GradeLevelId.Should().Be(gradeLevel.Id, "the winning row converged onto OUR requested grade");
        publisher.Enqueued.OfType<StudentEnrolled>().Should().BeEmpty("our insert never committed, so no StudentEnrolled of our own");
        publisher.Enqueued.OfType<StudentEnrollmentUpdated>().Should().ContainSingle("convergence is published as an update");
    }

    // ── Phase 5: Feature-flagged enrollment validation (plan §11) ───────────

    [TestMethod]
    public async Task AgeValidation_TooYoung_ThrowsStudentAgeViolationException()
    {
        using var s = new StudentsTestScope("enroll-age-young");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher, flagEnabled: true, minAge: 6, maxAge: 8);

        var gradeLevel = s.Db.GradeLevels.Single();
        var student = SeedStudent(s, new DateOnly(2020, 1, 15), GenderMale); // 5 yrs, below min 6

        var act = () => h.HandleAsync(new EnrollStudent(student.Id, ActivePeriodId, gradeLevel.CodedValueId, null, new DateOnly(2025, 9, 1)));

        (await act.Should().ThrowAsync<StudentAgeViolationException>())
            .Which.Message.Should().Contain("is 5 years old").And.Contain("requires age within");
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(0,
            "no enrollment row must be persisted when the age guard rejects the command");
    }

    [TestMethod]
    public async Task AgeValidation_TooOld_ThrowsStudentAgeViolationException()
    {
        using var s = new StudentsTestScope("enroll-age-old");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher, flagEnabled: true, minAge: 6, maxAge: 8);

        var gradeLevel = s.Db.GradeLevels.Single();
        var student = SeedStudent(s, new DateOnly(2014, 1, 15), GenderMale); // 11 yrs, above max 8

        var act = () => h.HandleAsync(new EnrollStudent(student.Id, ActivePeriodId, gradeLevel.CodedValueId, null, new DateOnly(2025, 9, 1)));

        (await act.Should().ThrowAsync<StudentAgeViolationException>())
            .Which.Message.Should().Contain("is 11 years old").And.Contain("requires age within");
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(0,
            "no enrollment row must be persisted when the age guard rejects the command");
    }

    [TestMethod]
    public async Task AgeValidation_WithinRange_Persists()
    {
        using var s = new StudentsTestScope("enroll-age-ok");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher, flagEnabled: true, minAge: 6, maxAge: 8);

        var gradeLevel = s.Db.GradeLevels.Single();
        var student = SeedStudent(s, new DateOnly(2018, 1, 15), GenderMale); // 7 yrs, within [6,8]

        var id = await h.HandleAsync(new EnrollStudent(student.Id, ActivePeriodId, gradeLevel.CodedValueId, null, new DateOnly(2025, 9, 1)));

        id.Should().NotBeEmpty();
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(1,
            "the enrollment persists when the student's age is within the grade level's range");
    }

    [TestMethod]
    public async Task GenderValidation_Mismatch_ThrowsStudentGenderViolationException()
    {
        using var s = new StudentsTestScope("enroll-gender-mismatch");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher, flagEnabled: true, allowedGender: GenderMale);

        var gradeLevel = s.Db.GradeLevels.Single();
        var student = SeedStudent(s, new DateOnly(2018, 1, 15), GenderFemale);

        var act = () => h.HandleAsync(new EnrollStudent(student.Id, ActivePeriodId, gradeLevel.CodedValueId, null, new DateOnly(2025, 9, 1)));

        (await act.Should().ThrowAsync<StudentGenderViolationException>())
            .Which.Message.Should().Contain("does not match the allowed gender");
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(0,
            "no enrollment row must be persisted when the gender guard rejects the command");
    }

    [TestMethod]
    public async Task GenderValidation_NullAllowed_Persists()
    {
        using var s = new StudentsTestScope("enroll-gender-coed");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher, flagEnabled: true, allowedGender: null);

        var gradeLevel = s.Db.GradeLevels.Single();
        var student = SeedStudent(s, new DateOnly(2018, 1, 15), GenderFemale);

        var id = await h.HandleAsync(new EnrollStudent(student.Id, ActivePeriodId, gradeLevel.CodedValueId, null, new DateOnly(2025, 9, 1)));

        id.Should().NotBeEmpty();
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(1,
            "the enrollment persists when the grade level has no gender restriction (co-ed)");
    }

    [TestMethod]
    public async Task MultipleActiveEnrollments_ThrowsMultipleActiveEnrollmentsException()
    {
        using var s = new StudentsTestScope("enroll-multi-active");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher, flagEnabled: true);

        var gradeLevel = s.Db.GradeLevels.Single();
        var student = SeedStudent(s, new DateOnly(2018, 1, 15), GenderMale);

        // Seed an ACTIVE enrollment for the student in a DIFFERENT period (the
        // upsert only reroutes same-period resubmits; an active enrollment in any
        // other period still makes this a genuinely-new enrollment, which the
        // single-active rule rejects while the flag is on).
        var otherPeriodId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        s.Db.StudentEnrollments.Add(StudentEnrollment.Create(student.Id, otherPeriodId, gradeLevel.Id));
        s.Db.SaveChanges();

        var act = () => h.HandleAsync(new EnrollStudent(student.Id, ActivePeriodId, gradeLevel.CodedValueId, null, new DateOnly(2025, 9, 2)));

        (await act.Should().ThrowAsync<MultipleActiveEnrollmentsException>())
            .Which.Message.Should().Contain("already has");
        (await s.Db.StudentEnrollments.CountAsync(e => e.PeriodId == ActivePeriodId)).Should().Be(0,
            "no enrollment row must be persisted when the single-active guard rejects the command");
    }

    [TestMethod]
    public async Task FeatureFlag_Disabled_SkipsValidation_AndPersists()
    {
        using var s = new StudentsTestScope("enroll-flag-off");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        // Flag OFF — grade level has rules, student violates them, but validation
        // is skipped so the enrollment persists (backward-compatible default).
        var h = await NewHandler(s, periods, publisher, flagEnabled: false, minAge: 6, maxAge: 8);

        var gradeLevel = s.Db.GradeLevels.Single();
        var student = SeedStudent(s, new DateOnly(2020, 1, 15), GenderMale); // 5 yrs, below min

        var id = await h.HandleAsync(new EnrollStudent(student.Id, ActivePeriodId, gradeLevel.CodedValueId, null, new DateOnly(2025, 9, 1)));

        id.Should().NotBeEmpty();
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(1,
            "with the flag off, validation is skipped and the enrollment persists even though the student violates the age rule");
    }

    [TestMethod]
    public async Task FeatureFlag_Enabled_NoGradeLevelRules_Persists()
    {
        using var s = new StudentsTestScope("enroll-flag-on-no-rules");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        // Flag ON but grade level has no rules (all null) → no restriction.
        var h = await NewHandler(s, periods, publisher, flagEnabled: true);

        var gradeLevel = s.Db.GradeLevels.Single();
        var student = SeedStudent(s, new DateOnly(2010, 1, 15), GenderFemale);

        var id = await h.HandleAsync(new EnrollStudent(student.Id, ActivePeriodId, gradeLevel.CodedValueId, null, new DateOnly(2025, 9, 1)));

        id.Should().NotBeEmpty();
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(1,
            "with the flag on but no grade-level rules, the enrollment persists (null rules = no restriction)");
    }

    /// <summary>Seeds a <see cref="Student"/> with the given demographics directly
    /// into the test's in-memory DbContext. The DbContext's tenant interceptor
    /// stamps the entity with the scope's tenant so the StudentRepository's
    /// tenant-scoped query filter can find it. Returns the entity so the test
    /// can use its generated <see cref="Domain.Student.Id"/> in the enrollment
    /// command.</summary>
    private static Student SeedStudent(StudentsTestScope s, DateOnly dateOfBirth, Guid genderCodedValueId)
    {
        var student = Student.Create("STU-TEST", "Test", "Student", dateOfBirth, genderCodedValueId);
        s.Db.Students.Add(student);
        s.Db.SaveChanges();
        return student;
    }

    /// <summary>Stub <see cref="ICodedValuesApiClient"/> that returns a
    /// stream whose <c>gradeLevel</c> attribute value matches the
    /// GradeLevel's CodedValueId seeded by the test. Stream validation
    /// is the only behavior exercised by these tests; the stub keeps
    /// the test hermetic (no HTTP).</summary>
    private sealed class StubCodedValuesApiClient : ICodedValuesApiClient
    {
        public Task<StreamCodedValueDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            // Look up the seeded GradeLevel's CodedValueId from the test's DbContext
            // is not available here (no DI), so we use a fixed Guid that the
            // test will also use when seeding the GradeLevel. See the test
            // setup for the matching seed.
            return Task.FromResult<StreamCodedValueDto?>(new StreamCodedValueDto(
                id, "GRSTREAMS_TEST", "Test Stream", null,
                null, "GRSTREAMS", false, 0,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                new[] { new StreamAttributeDto("gradeLevel", "22222222-2222-2222-2222-222222222223") }));
        }
    }

    /// <summary>Minimal <see cref="IGradeLevelRepository"/> backed by the
    /// test's in-memory DbContext. Returns the GradeLevel seeded by
    /// <see cref="StudentsTestScope"/>. Avoids spinning up the real
    /// repository's include graph for these handler-only tests.</summary>
    private sealed class InMemoryGradeLevelRepository(StudentsDbContext db) : IGradeLevelRepository
    {
        public async Task<GradeLevel?> GetAsync(Guid id, CancellationToken ct = default) =>
            await db.GradeLevels.FirstOrDefaultAsync(x => x.Id == id, ct);

        public Task<GradeLevel?> GetByCodedValueIdAsync(Guid codedValueId, CancellationToken ct = default) =>
            db.GradeLevels.FirstOrDefaultAsync(x => x.CodedValueId == codedValueId, ct);

        public async Task<GradeLevelDto[]> ListAsync(CancellationToken ct = default) =>
            await db.GradeLevels
                .AsNoTracking()
                .OrderBy(x => x.Level)
                .Select(x => new GradeLevelDto(x.Id, x.CodedValueId, x.Level, x.Name, x.DisplayOrder, 0, 0, x.CreatedAt, x.UpdatedAt, x.MinAge, x.MaxAge, x.AllowedGenderCodedValueId))
                .ToArrayAsync(ct);

        public async Task AddAsync(GradeLevel gradeLevel, CancellationToken ct = default)
        {
            await db.GradeLevels.AddAsync(gradeLevel, ct);
            await db.SaveChangesAsync(ct);
        }

        // The in-memory provider enforces neither unique indexes nor Postgres
        // SQLSTATEs, so the conflict branch cannot fire here — behave as the
        // no-conflict path. The race contract itself is pinned by the stub-based
        // ConcurrentMaterialization tests below.
        public async Task<GradeLevel> AddOrReuseAsync(GradeLevel candidate, CancellationToken ct = default)
        {
            await AddAsync(candidate, ct);
            return candidate;
        }

        public async Task UpdateAsync(GradeLevel gradeLevel, CancellationToken ct = default)
        {
            db.GradeLevels.Update(gradeLevel);
            await db.SaveChangesAsync(ct);
        }
    }

    // ── Option B: server-side CodedValueId → GradeLevelId join ─────────────

    [TestMethod]
    public async Task BlockedGradeLevel_ThrowsGradeLevelEnrollmentBlockedException()
    {
        using var s = new StudentsTestScope("enroll-blocked-grade");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher);

        var gradeLevel = s.Db.GradeLevels.Single();
        gradeLevel.Update(gradeLevel.Level, gradeLevel.Name, gradeLevel.DisplayOrder,
            gradeLevel.MinAge, gradeLevel.MaxAge, gradeLevel.AllowedGenderCodedValueId,
            isBlockedFromEnrollment: true);
        s.Db.SaveChanges();

        var student = SeedStudent(s, new DateOnly(2018, 1, 15), GenderMale);
        var act = () => h.HandleAsync(new EnrollStudent(student.Id, ActivePeriodId, gradeLevel.CodedValueId, null, null));

        (await act.Should().ThrowAsync<GradeLevelEnrollmentBlockedException>())
            .Which.Message.Should().Contain("blocked from enrollment",
                "the client no longer filters blocked grades — the server is the enforcement point");
        (await s.Db.StudentEnrollments.CountAsync()).Should().Be(0);
        publisher.Enqueued.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UnknownCodedValueId_MaterializesGradeLevel_AndEnrolls()
    {
        using var s = new StudentsTestScope("enroll-materialize");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var h = await NewHandler(s, periods, publisher);

        var unknownCodedValueId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var student = SeedStudent(s, new DateOnly(2018, 1, 15), GenderMale);

        // No GradeLevel row exists for this coded value; the handler
        // materializes one from the coded value (the stub API returns a
        // matching row) and enrolls against it.
        var id = await h.HandleAsync(new EnrollStudent(student.Id, ActivePeriodId, unknownCodedValueId, null, null));

        id.Should().NotBeEmpty();
        var row = await s.Db.StudentEnrollments.SingleAsync();
        var materialized = await s.Db.GradeLevels.SingleAsync(g => g.CodedValueId == unknownCodedValueId);
        row.GradeLevelId.Should().Be(materialized.Id);
    }

    // ── Materialization race: concurrent first-time enrolls ─────────────────

    /// <summary><see cref="IGradeLevelRepository"/> decorator that simulates the
    /// Postgres unique-index loss: the candidate insert "fails" and a
    /// pre-seeded winner row is returned by <see cref="AddOrReuseAsync"/>,
    /// exactly what <see cref="GradeLevelRepository.AddOrReuseAsync"/> does on
    /// a real 23505 conflict. Pins the handler-side contract: the enrollment
    /// must reference the WINNING row and publish exactly one event.</summary>
    private sealed class RacingGradeLevelRepository(IGradeLevelRepository inner, GradeLevel winner) : IGradeLevelRepository
    {
        public int ConflictCount;

        public Task<GradeLevel?> GetAsync(Guid id, CancellationToken ct = default) => inner.GetAsync(id, ct);

        // Simulates the read racing AHEAD of the concurrent commit: while the
        // conflict has not yet "happened", the winning row is invisible (the
        // reader's snapshot predates it); after the simulated 23505, reads see it.
        public Task<GradeLevel?> GetByCodedValueIdAsync(Guid codedValueId, CancellationToken ct = default) =>
            ConflictCount == 0 && codedValueId == winner.CodedValueId
                ? Task.FromResult<GradeLevel?>(null)
                : inner.GetByCodedValueIdAsync(codedValueId, ct);

        public Task<GradeLevelDto[]> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);
        public Task AddAsync(GradeLevel gradeLevel, CancellationToken ct = default) => inner.AddAsync(gradeLevel, ct);
        public Task UpdateAsync(GradeLevel gradeLevel, CancellationToken ct = default) => inner.UpdateAsync(gradeLevel, ct);

        public async Task<GradeLevel> AddOrReuseAsync(GradeLevel candidate, CancellationToken ct = default)
        {
            ConflictCount++;
            return await Task.FromResult(winner);
        }
    }

    private static async Task<(EnrollStudentHandler Handler, RacingGradeLevelRepository Repo)> NewHandlerWithRacingRepo(
        StudentsTestScope s,
        StubActivePeriodProvider periods,
        RecordingPublisher publisher)
    {
        // Seed the WINNING GradeLevel — the row the concurrent enroll already
        // committed before our insert hit the unique index.
        var raceCodedValueId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var winner = GradeLevel.Create(raceCodedValueId, level: 1, name: "Winner", displayOrder: 1);
        s.Db.GradeLevels.Add(winner);
        s.Db.SaveChanges();

        var gradeCodedValueId = Guid.Parse("22222222-2222-2222-2222-222222222223");
        s.Db.GradeLevels.Add(GradeLevel.Create(gradeCodedValueId, level: 2, name: "Grade 1", displayOrder: 1));
        s.Db.SaveChanges();

        var racingRepo = new RacingGradeLevelRepository(new InMemoryGradeLevelRepository(s.Db), winner);

        var handler = new EnrollStudentHandler(
            new StudentEnrollmentRepository(s.Db),
            periods,
            racingRepo,
            new StubCodedValuesApiClient(),
            publisher,
            s.Cache,
            NullLogger<EnrollStudentHandler>.Instance,
            new StubFeatureFlagService(false),
            new StudentRepository(s.Db),
            new CompositeEnrollmentSpecification(new ILeafEnrollmentSpecification[]
            {
                new AgeRangeSpecification(),
                new GenderRestrictionSpecification(),
                new SingleActiveEnrollmentSpecification()
            }),
            s.Tenants,
            s.Db,
            TestActor);

        return (handler, racingRepo);
    }

    /// <summary><see cref="IStudentEnrollmentRepository"/> decorator that simulates
    /// the Postgres unique-index loss on the enrollment insert: the lookup runs
    /// against the real repository (the winning row is hidden until the simulated
    /// conflict fires), then <see cref="AddOrReuseAsync"/> returns the pre-seeded
    /// winner — exactly what <see cref="StudentEnrollmentRepository.AddOrReuseAsync"/>
    /// does on a real 23505 on ix_student_enrollments_tenant_student_period. Pins
    /// the handler-side convergence contract of the enroll upsert.</summary>
    private sealed class RacingEnrollmentRepository(IStudentEnrollmentRepository inner, Func<StudentEnrollment?> invisibleUntilConflict)
        : IStudentEnrollmentRepository
    {
        public int ConflictCount;

        public Task<StudentEnrollment?> GetAsync(Guid id, CancellationToken ct = default) => inner.GetAsync(id, ct);
        public Task UpdateAsync(StudentEnrollment enrollment, CancellationToken ct = default) => inner.UpdateAsync(enrollment, ct);
        public Task<StudentEnrollmentDto[]> ListByPeriodAsync(Guid periodId, CancellationToken ct = default) => inner.ListByPeriodAsync(periodId, ct);
        public Task<StudentEnrollmentDto[]> ListByStudentAsync(Guid studentId, CancellationToken ct = default) => inner.ListByStudentAsync(studentId, ct);
        public Task<StudentEnrollment[]> GetActiveEnrollmentsForPeriodAsync(Guid periodId, CancellationToken ct = default) => inner.GetActiveEnrollmentsForPeriodAsync(periodId, ct);
        public Task<StudentEnrollment[]> GetActiveEnrollmentsByStudentAsync(Guid studentId, CancellationToken ct = default) => inner.GetActiveEnrollmentsByStudentAsync(studentId, ct);
        public Task AddAsync(StudentEnrollment enrollment, CancellationToken ct = default) => inner.AddAsync(enrollment, ct);

        // The upsert lookup races AHEAD of the concurrent commit: while the conflict
        // has not yet "happened", the winning row is invisible; afterwards reads see it.
        public Task<StudentEnrollment?> GetActiveEnrollmentByStudentAndPeriodAsync(
            Guid studentId, Guid periodId, CancellationToken ct = default)
        {
            var visible = invisibleUntilConflict();
            return Task.FromResult(ConflictCount == 0 ? null : visible);
        }

        public async Task<StudentEnrollment> AddOrReuseAsync(StudentEnrollment candidate, CancellationToken ct = default)
        {
            // First insert loses the simulated 23505; subsequent inserts commit.
            if (ConflictCount++ > 0)
            {
                await inner.AddAsync(candidate, ct);
                return candidate;
            }
            return invisibleUntilConflict()
                ?? throw new InvalidOperationException("Simulated race produced no winner row.");
        }
    }

    [TestMethod]
    public async Task ConcurrentMaterialization_LosesRace_EnrollsAgainstWinningRow()
    {
        using var s = new StudentsTestScope("enroll-race-loser");
        var periods = new StubActivePeriodProvider { Active = ActivePeriod() };
        var publisher = new RecordingPublisher();
        var (h, racingRepo) = await NewHandlerWithRacingRepo(s, periods, publisher);

        var raceCodedValueId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var student = SeedStudent(s, new DateOnly(2018, 1, 15), GenderMale);

        var id = await h.HandleAsync(new EnrollStudent(student.Id, ActivePeriodId, raceCodedValueId, null, null));

        id.Should().NotBeEmpty("the command succeeds despite losing the materialization race");
        racingRepo.ConflictCount.Should().Be(1, "the handler routed the insert through AddOrReuseAsync");

        var row = await s.Db.StudentEnrollments.SingleAsync();
        var winner = await s.Db.GradeLevels.SingleAsync(g => g.CodedValueId == raceCodedValueId);
        row.GradeLevelId.Should().Be(winner.Id,
            "the enrollment references the WINNING row, not the losing candidate — a second GradeLevel for the same coded value must not exist");
        (await s.Db.GradeLevels.CountAsync(g => g.CodedValueId == raceCodedValueId)).Should().Be(1,
            "exactly one GradeLevel row per (tenant, coded_value) survives the race");
        publisher.Enqueued.OfType<StudentEnrolled>().Should().ContainSingle("exactly one event is published once the race resolves");
    }
}
