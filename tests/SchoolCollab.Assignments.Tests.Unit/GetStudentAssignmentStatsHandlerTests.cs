using FluentAssertions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetStudentAssignmentStats;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Issue #444 (layer L2a) — the count rules of
/// <see cref="GetStudentAssignmentStatsQueryHandler"/>, folded over an explicit per-assignment fixture
/// (decision A(ii): the arithmetic is C#, so each rule is a plain assertion rather than SQL no test can
/// reach). The fixture rows are the read's own projection, so these tests pin the FOLD; the read's WHERE
/// clause is pinned by <see cref="StudentAssignmentStatsRepositoryTenantTests"/>.
/// </summary>
[TestClass]
public class GetStudentAssignmentStatsHandlerTests
{
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-00000000a001");

    /// <summary>The read's only job under test: hand back the rows and count how often it was asked.</summary>
    private sealed class FakeStatsRepository(List<StudentAssignmentStatRow> rows) : IStudentAssignmentStatsRepository
    {
        public int Calls { get; private set; }

        public Task<List<StudentAssignmentStatRow>> ListStudentAssignmentStatRowsAsync(Guid studentId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(rows);
        }
    }

    private static Task<StudentAssignmentStatsDto> HandleAsync(
        FakeTimeProvider clock,
        params StudentAssignmentStatRow[] rows)
        => new GetStudentAssignmentStatsQueryHandler(new FakeStatsRepository([.. rows]), clock)
            .HandleAsync(new GetStudentAssignmentStats(StudentId));

    /// <summary>One recipient-linked assignment. Every flag defaults to "nothing has happened to it".</summary>
    private static StudentAssignmentStatRow Row(
        AssignmentStatus status = AssignmentStatus.Published,
        DateTimeOffset? availableFromUtc = null,
        bool mandatoryReview = false,
        bool hasFinalizedSubmission = false,
        ReviewState? reviewState = null,
        bool hasReviewedGate = false)
        => new(status, availableFromUtc, mandatoryReview, hasFinalizedSubmission, reviewState, hasReviewedGate);

    [TestMethod]
    public async Task HandleAsync_FoldsTheSixCounts_FromAPerAssignmentFixture()
    {
        var clock = new FakeTimeProvider();
        var now = clock.GetUtcNow();

        var stats = await HandleAsync(clock,
            // never sent — counted as assigned and nothing else
            Row(AssignmentStatus.Draft),
            // sent, no submission
            Row(),
            // sent, mandatory review, submitted but unmarked, gate not reviewed
            Row(mandatoryReview: true, hasFinalizedSubmission: true, reviewState: ReviewState.Pending),
            // sent, mandatory review, submitted AND marked, gate reviewed
            Row(mandatoryReview: true, hasFinalizedSubmission: true, reviewState: ReviewState.Graded, hasReviewedGate: true),
            // Q126: scheduled with its publish time passed counts as sent
            Row(AssignmentStatus.Scheduled, availableFromUtc: now.AddHours(-1)),
            // Q126: scheduled in the future has not been sent
            Row(AssignmentStatus.Scheduled, availableFromUtc: now.AddHours(1)),
            // sent, mandatory review, held by the guardian gate — awaiting review, not awaiting submission
            Row(mandatoryReview: true));

        stats.Assigned.Should().Be(7, "every recipient-linked row counts as assigned, including the Draft");
        stats.SentOut.Should().Be(5, "the Draft and the future Scheduled row have not been sent");
        stats.Submitted.Should().Be(2, "only the two finalized submissions count");
        stats.Marked.Should().Be(1, "only the finalized AND graded submission is marked");
        stats.ReviewRequired.Should().Be(3, "three SENT assignments carry MandatoryReview (the unsent Draft is not one)");
        stats.Reviewed.Should().Be(1, "only one guardian gate was reviewed");

        // The two derived invariants the panel reads (they are not transmitted — they are arithmetic the
        // caller performs, so they are asserted here as the caller would compute them).
        (stats.Submitted - stats.Marked).Should().Be(1, "AwaitingMarking = Submitted − Marked");
        (stats.ReviewRequired - stats.Reviewed).Should().Be(2, "AwaitingReview = ReviewRequired − Reviewed");
    }

    [TestMethod]
    public async Task HandleAsync_Marked_RequiresTheSameSubmissionFinalizedAndGraded()
    {
        var clock = new FakeTimeProvider();

        var stats = await HandleAsync(clock,
            // Case 1: submitted but not yet marked — the teacher has not graded it.
            Row(hasFinalizedSubmission: true, reviewState: ReviewState.Pending),
            // Case 2: graded without a finalization — the domain permits it (ApplyReview is independent of
            // the sign-off chain), and it must NOT be marked: marking is a post-finalization outcome.
            Row(hasFinalizedSubmission: false, reviewState: ReviewState.Graded));

        stats.Submitted.Should().Be(1, "only case 1 carries FinalizedAt");
        stats.Marked.Should().Be(0,
            "grading alone never counts as marked — the fold ANDs HasFinalizedSubmission with ReviewState.Graded");
        stats.Marked.Should().BeLessThanOrEqualTo(stats.Submitted,
            "every marked assignment is also a submitted one — a Graded-only row would break this");
        (stats.Submitted - stats.Marked).Should().Be(1, "case 1 lands in awaiting marking");
    }

    [TestMethod]
    public async Task HandleAsync_GateHeldAssignment_LandsInAwaitingReview_NeverAwaitingSubmission()
    {
        var clock = new FakeTimeProvider();

        var stats = await HandleAsync(clock, Row(mandatoryReview: true, hasReviewedGate: false));

        stats.SentOut.Should().Be(1, "the assignment has been sent to the student");
        stats.ReviewRequired.Should().Be(1, "it is gated on guardian review");
        stats.Reviewed.Should().Be(0, "the gate has not been reviewed");
        stats.Submitted.Should().Be(0, "a gate-held assignment is not an awaiting-submission one");
        stats.Marked.Should().Be(0);
        (stats.ReviewRequired - stats.Reviewed).Should().Be(1, "it lands in awaiting review");
    }

    [TestMethod]
    public async Task HandleAsync_ReviewRequired_CountsOnlyMandatoryReviewAmongSentAssignments()
    {
        var clock = new FakeTimeProvider();

        var stats = await HandleAsync(clock,
            // mandatory review, but never sent — cannot be gated
            Row(AssignmentStatus.Draft, mandatoryReview: true),
            // sent, but not gated
            Row(mandatoryReview: false),
            // sent and gated
            Row(mandatoryReview: true));

        stats.ReviewRequired.Should().Be(1,
            "a Draft cannot be gated and a non-mandatory assignment is not part of the review axis");
        stats.SentOut.Should().Be(2);
        stats.ReviewRequired.Should().BeLessThanOrEqualTo(stats.SentOut,
            "ReviewRequired ≤ SentOut — an unsent draft can never be gated");
    }

    [TestMethod]
    public async Task HandleAsync_Scheduled_CountsAsSentOnlyOnceItsPublishTimeHasPassed()
    {
        var clock = new FakeTimeProvider();
        var now = clock.GetUtcNow();

        var repository = new FakeStatsRepository(
        [
            Row(AssignmentStatus.Scheduled, availableFromUtc: now.AddMinutes(-1)),
            Row(AssignmentStatus.Scheduled, availableFromUtc: now.AddMinutes(1)),
            // A Scheduled row with no publish moment at all is never "sent".
            Row(AssignmentStatus.Scheduled),
        ]);
        var handler = new GetStudentAssignmentStatsQueryHandler(repository, clock);

        var before = await handler.HandleAsync(new GetStudentAssignmentStats(StudentId));

        before.SentOut.Should().Be(1, "only the Scheduled row whose publish moment has already passed");
        before.Assigned.Should().Be(3, "all three are recipient-linked regardless of status");

        clock.Advance(TimeSpan.FromMinutes(2));

        var after = await handler.HandleAsync(new GetStudentAssignmentStats(StudentId));

        after.SentOut.Should().Be(2,
            "the clock is the injected TimeProvider, so advancing it — and only it — flips the second row to sent");
    }

    [TestMethod]
    public async Task HandleAsync_IsDrivenOnlyByTheStudent_TheSignatureCarriesNoPeriod()
    {
        // C6 / Q139: the counts join on the student, so there is no period parameter a caller could narrow
        // into an empty result set. The signature is the proof, and it is asserted rather than described —
        // a future "add a period filter" edit fails here.
        typeof(GetStudentAssignmentStats).GetProperties()
            .Should().ContainSingle("the query carries the student and nothing else")
            .Which.Name.Should().Be(nameof(GetStudentAssignmentStats.StudentId));

        var clock = new FakeTimeProvider();
        var repository = new FakeStatsRepository([Row(), Row(mandatoryReview: true), Row(AssignmentStatus.Draft)]);
        var handler = new GetStudentAssignmentStatsQueryHandler(repository, clock);

        var shortPlacement = await handler.HandleAsync(new GetStudentAssignmentStats(StudentId));
        var academicYear = await handler.HandleAsync(new GetStudentAssignmentStats(Guid.NewGuid()));

        shortPlacement.Should().Be(academicYear,
            "a student whose placement is a sub-period must yield the same counts as one on the academic "
            + "year for the same recipient-linked set — nothing but the student reaches the fold");
    }

    [TestMethod]
    public async Task HandleAsync_IssuesAConstantNumberOfRepositoryCalls_NoNPlusOne()
    {
        // C8 is an honest guard, not a discriminator: with a single repository call the criterion is
        // structurally true today, so this test protects the shape against a future per-row rewrite.
        var clock = new FakeTimeProvider();

        var one = new FakeStatsRepository([Row()]);
        await new GetStudentAssignmentStatsQueryHandler(one, clock)
            .HandleAsync(new GetStudentAssignmentStats(StudentId));

        var many = new FakeStatsRepository([.. Enumerable.Range(0, 25).Select(_ => Row())]);
        await new GetStudentAssignmentStatsQueryHandler(many, clock)
            .HandleAsync(new GetStudentAssignmentStats(StudentId));

        many.Calls.Should().Be(one.Calls,
            "a 25-assignment student must cost the same number of repository reads as a 1-assignment student");
        one.Calls.Should().Be(1, "the six counts come out of ONE round trip");
    }
}
