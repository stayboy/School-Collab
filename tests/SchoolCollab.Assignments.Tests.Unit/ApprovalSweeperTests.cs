using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.AutoReturnAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Worker.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>assignment-approval-workflow</c> (spec §3.4, §6; Q11/Q12) — the approval sweep:
/// <list type="bullet">
///   <item>the candidate read is cross-tenant but restricted to <b>Pending AND Draft</b> (a
///   Closed+Pending row is untouchable — the plan's WS2 <c>Close</c> finding) and projects the
///   derived policy-scope grade;</item>
///   <item>§3.4's order: due-date-passes wins, the window expiring returns the row, a null window
///   skips;</item>
///   <item>a per-grade <c>ApprovalWindowDays</c> override actually reaches the resolver;</item>
///   <item>the auto-return runs through <see cref="AutoReturnAssignmentCommand"/>, which needs no
///   principal (the whole reason it exists), attributed to the documented system sentinel;</item>
///   <item>per-candidate isolation: one failing row never stops the pass.</item>
/// </list>
/// The sweep clock is the injected <see cref="TimeProvider"/> — never the wall clock.
/// </summary>
[TestClass]
public class ApprovalSweeperTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid GradeId = Guid.Parse("22222222-2222-2222-2222-22222222222a");
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    // ── helpers ─────────────────────────────────────────────────────────

    private static (AssignmentsDbContext Db, AssignmentRepository Repo, TenantContextAccessor Accessor) BuildScope(string name)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(name));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AssignmentsDbContext>();
        db.Database.EnsureCreated();
        var provider = (TenantProvider)sp.GetRequiredService<ITenantProvider>();
        provider.SetTenant(new TenantContext(TenantA, "SchoolA", TenantType.School));
        return (db, new AssignmentRepository(db), new TenantContextAccessor(provider));
    }

    private static TenantContextAccessor Accessor() =>
        new(new TenantProvider());

    private static HybridCache NewCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    private static Assignment NewDraft(Guid tenantId, DateTimeOffset? dueDate = null, Guid? gradeId = null)
    {
        var assignment = Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, dueDate, null, TeacherId)
            .WithTenant(tenantId);
        if (gradeId is { } grade)
        {
            assignment.SetTargets([(TargetKind.GradeLevel, (Guid?)grade)], tenantId, []);
        }
        return assignment;
    }

    private static Assignment Pending(Guid tenantId, DateTimeOffset? dueDate = null, Guid? gradeId = null)
    {
        var assignment = NewDraft(tenantId, dueDate, gradeId);
        assignment.SubmitForApproval();
        return assignment;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static ApprovalSweeperHarness Harness(TimeProvider timeProvider, EffectiveAssignmentPolicy? policy = null)
    {
        var autoReturn = new Mock<ICommandHandler<AutoReturnAssignmentCommand>>();
        var returned = new List<AutoReturnAssignmentCommand>();
        autoReturn
            .Setup(h => h.HandleAsync(It.IsAny<AutoReturnAssignmentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<AutoReturnAssignmentCommand, CancellationToken>((command, _) => returned.Add(command))
            .Returns(Task.CompletedTask);

        var resolver = new FakeAssignmentPolicyResolver();
        if (policy is not null)
        {
            resolver.Policy = policy;
        }

        return new ApprovalSweeperHarness(autoReturn, returned, resolver);
    }

    private sealed record ApprovalSweeperHarness(
        Mock<ICommandHandler<AutoReturnAssignmentCommand>> AutoReturn,
        List<AutoReturnAssignmentCommand> Returned,
        FakeAssignmentPolicyResolver Resolver);

    private static EffectiveAssignmentPolicy Policy(int? windowDays) =>
        new EffectiveAssignmentPolicyResolver().Resolve(
            new AssignmentPolicyFields { ApprovalWindowDays = windowDays }, gradeOverride: null);

    // ══ Candidate read ══════════════════════════════════════════════════

    [TestMethod]
    public async Task CandidateRead_ReturnsPendingDrafts_AcrossTenants()
    {
        var (db, repo, accessor) = BuildScope("approval-candidates-" + Guid.NewGuid());
        await using var _db = db;
        var pendingA = Pending(TenantA);
        var pendingB = Pending(TenantB);
        var notSubmitted = NewDraft(TenantA);
        db.Assignments.AddRange(pendingA, pendingB, notSubmitted);
        // Seeding two tenants in one context is a test-only cross-tenant write.
        using (accessor.SuppressTenantGuard())
        {
            await db.SaveChangesAsync();
        }

        var candidates = await repo.ListPendingApprovalSweepCandidatesAsync();

        candidates.Select(c => c.AssignmentId).Should().BeEquivalentTo([pendingA.Id, pendingB.Id],
            "the read is the sanctioned cross-tenant one and selects Pending rows only");
    }

    [TestMethod]
    public async Task CandidateRead_ExcludesAClosedPendingRow()
    {
        var (db, repo, accessor) = BuildScope("approval-candidates-closed-" + Guid.NewGuid());
        await using var _db = db;
        var pending = Pending(TenantA);
        var closedPending = Pending(TenantA);
        closedPending.Close(); // the WS2 finding: Close() does NOT refuse a Pending row
        db.Assignments.AddRange(pending, closedPending);
        await db.SaveChangesAsync();

        var candidates = await repo.ListPendingApprovalSweepCandidatesAsync();

        candidates.Select(c => c.AssignmentId).Should().ContainSingle().Which.Should().Be(pending.Id,
            "auto-returning a row that can be neither edited nor resubmitted would be a trap (§3.4)");
    }

    [TestMethod]
    public async Task CandidateRead_ProjectsTheDerivedPolicyGrade()
    {
        var (db, repo, accessor) = BuildScope("approval-candidates-grade-" + Guid.NewGuid());
        await using var _db = db;
        var graded = Pending(TenantA, gradeId: GradeId);
        var unGraded = Pending(TenantA);
        db.Assignments.AddRange(graded, unGraded);
        await db.SaveChangesAsync();

        var candidates = await repo.ListPendingApprovalSweepCandidatesAsync();

        candidates.Single(c => c.AssignmentId == graded.Id).PolicyGradeId.Should().Be(GradeId,
            "without the projected grade a per-grade approval window would silently never apply");
        candidates.Single(c => c.AssignmentId == unGraded.Id).PolicyGradeId.Should().BeNull();
    }

    [TestMethod]
    public async Task CandidateRead_CarriesTheSubmissionStampAndDueDate()
    {
        var (db, repo, accessor) = BuildScope("approval-candidates-stamps-" + Guid.NewGuid());
        await using var _db = db;
        var due = Now.AddDays(-1);
        var pending = Pending(TenantA, dueDate: due);
        db.Assignments.Add(pending);
        await db.SaveChangesAsync();

        var candidate = (await repo.ListPendingApprovalSweepCandidatesAsync()).Single();

        candidate.SubmittedAt.Should().Be(pending.SubmittedAt);
        candidate.DueDate.Should().Be(due);
        candidate.TenantId.Should().Be(TenantA);
    }

    // ══ §3.4 rules ══════════════════════════════════════════════════════

    [TestMethod]
    public async Task WindowExpired_ReturnsTheRow_WithTheWindowReason_AndTheSystemSentinel()
    {
        var harness = Harness(new FixedTimeProvider(Now), Policy(windowDays: 3));
        var candidate = new AssignmentApprovalSweepCandidate(
            Guid.NewGuid(), "Math", TenantA, PolicyGradeId: null,
            SubmittedAt: Now.AddDays(-4), DueDate: null);

        var returned = await ApprovalSweeper.ReturnExpiredAsync(
            [candidate], harness.Resolver, harness.AutoReturn.Object,
            Accessor(), new FixedTimeProvider(Now), NullLogger.Instance);

        returned.Should().Be(1);
        harness.Returned.Should().ContainSingle();
        harness.Returned[0].Reason.Should().Be(ApprovalSweeper.WindowExpiredReason);
        harness.Returned[0].ApproverId.Should().Be(ApprovalSweeper.SystemApproverId);
        ApprovalSweeper.SystemApproverId.Should().NotBe(Guid.Empty,
            "the domain refuses Guid.Empty, so the sentinel must be a real, documented id");
    }

    [TestMethod]
    public async Task DueDatePassed_Wins_WhenBothRulesApply()
    {
        var harness = Harness(new FixedTimeProvider(Now), Policy(windowDays: 1));
        var candidate = new AssignmentApprovalSweepCandidate(
            Guid.NewGuid(), "Math", TenantA, PolicyGradeId: null,
            SubmittedAt: Now.AddDays(-10), DueDate: Now.AddHours(-1));

        await ApprovalSweeper.ReturnExpiredAsync(
            [candidate], harness.Resolver, harness.AutoReturn.Object,
            Accessor(), new FixedTimeProvider(Now), NullLogger.Instance);

        harness.Returned.Should().ContainSingle();
        harness.Returned[0].Reason.Should().Be(ApprovalSweeper.DueDatePassedReason,
            "an approved-but-overdue assignment would be pointless — the due date outranks the window");
    }

    [TestMethod]
    public async Task DueDatePassed_Fires_EvenWhenNoWindowIsConfigured()
    {
        var harness = Harness(new FixedTimeProvider(Now), Policy(windowDays: null));
        var candidate = new AssignmentApprovalSweepCandidate(
            Guid.NewGuid(), "Math", TenantA, PolicyGradeId: null,
            SubmittedAt: Now.AddDays(-2), DueDate: Now.AddDays(-1));

        await ApprovalSweeper.ReturnExpiredAsync(
            [candidate], harness.Resolver, harness.AutoReturn.Object,
            Accessor(), new FixedTimeProvider(Now), NullLogger.Instance);

        harness.Returned.Should().ContainSingle()
            .Which.Reason.Should().Be(ApprovalSweeper.DueDatePassedReason);
    }

    [TestMethod]
    public async Task NullWindow_WithNoPassedDueDate_SkipsTheRow()
    {
        var harness = Harness(new FixedTimeProvider(Now), Policy(windowDays: null));
        var candidate = new AssignmentApprovalSweepCandidate(
            Guid.NewGuid(), "Math", TenantA, PolicyGradeId: null,
            SubmittedAt: Now.AddDays(-400), DueDate: Now.AddDays(30));

        var returned = await ApprovalSweeper.ReturnExpiredAsync(
            [candidate], harness.Resolver, harness.AutoReturn.Object,
            Accessor(), new FixedTimeProvider(Now), NullLogger.Instance);

        returned.Should().Be(0);
        harness.Returned.Should().BeEmpty("no window means no deadline — the built-in default never invents one");
    }

    [TestMethod]
    public async Task WindowStillOpen_SkipsTheRow()
    {
        var harness = Harness(new FixedTimeProvider(Now), Policy(windowDays: 5));
        var candidate = new AssignmentApprovalSweepCandidate(
            Guid.NewGuid(), "Math", TenantA, PolicyGradeId: null,
            SubmittedAt: Now.AddDays(-1), DueDate: null);

        var returned = await ApprovalSweeper.ReturnExpiredAsync(
            [candidate], harness.Resolver, harness.AutoReturn.Object,
            Accessor(), new FixedTimeProvider(Now), NullLogger.Instance);

        returned.Should().Be(0);
        harness.Returned.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StoredNonPositiveWindow_IsTreatedAsNoWindow_SoNothingIsReturned()
    {
        var harness = Harness(new FixedTimeProvider(Now), Policy(windowDays: 0));
        var candidate = new AssignmentApprovalSweepCandidate(
            Guid.NewGuid(), "Math", TenantA, PolicyGradeId: null,
            SubmittedAt: Now.AddDays(-30), DueDate: null);

        await ApprovalSweeper.ReturnExpiredAsync(
            [candidate], harness.Resolver, harness.AutoReturn.Object,
            Accessor(), new FixedTimeProvider(Now), NullLogger.Instance);

        harness.Returned.Should().BeEmpty("Q7's consumer half: a stored non-positive window is no window");
    }

    [TestMethod]
    public async Task CandidateGrade_IsPassedToTheResolver_SoAGradeOverrideApplies()
    {
        var harness = Harness(new FixedTimeProvider(Now), Policy(windowDays: 2));
        var candidate = new AssignmentApprovalSweepCandidate(
            Guid.NewGuid(), "Math", TenantA, PolicyGradeId: GradeId,
            SubmittedAt: Now.AddDays(-3), DueDate: null);

        await ApprovalSweeper.ReturnExpiredAsync(
            [candidate], harness.Resolver, harness.AutoReturn.Object,
            Accessor(), new FixedTimeProvider(Now), NullLogger.Instance);

        harness.Resolver.RequestedGradeLevelIds.Should().Equal([GradeId],
            "the sweep must resolve the row's policy-scope grade, not the tenant default");
        harness.Returned.Should().ContainSingle();
    }

    [TestMethod]
    public async Task OneFailingCandidate_DoesNotStopThePass()
    {
        var harness = Harness(new FixedTimeProvider(Now), Policy(windowDays: 1));
        var failing = new AssignmentApprovalSweepCandidate(
            Guid.NewGuid(), "Boom", TenantA, null, Now.AddDays(-5), null);
        var healthy = new AssignmentApprovalSweepCandidate(
            Guid.NewGuid(), "Math", TenantA, null, Now.AddDays(-5), null);

        harness.AutoReturn
            .Setup(h => h.HandleAsync(It.Is<AutoReturnAssignmentCommand>(c => c.AssignmentId == failing.AssignmentId), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssignmentNotFoundException(failing.AssignmentId));

        var returned = await ApprovalSweeper.ReturnExpiredAsync(
            [failing, healthy], harness.Resolver, harness.AutoReturn.Object,
            Accessor(), new FixedTimeProvider(Now), NullLogger.Instance);

        returned.Should().Be(1);
        harness.Returned.Select(c => c.AssignmentId).Should().Contain(healthy.AssignmentId);
    }

    // ══ The auto-return command itself ══════════════════════════════════

    [TestMethod]
    public async Task AutoReturnCommand_NeedsNoPrincipal_AndReturnsTheRowForRedo()
    {
        // The whole reason AutoReturnAssignmentCommand exists: the reject handler's ar-20 principal
        // guard would throw for any Worker caller under real auth. This handler has no ICurrentUser
        // dependency and no command-level policy resolve — it is constructible here with a repo, a
        // cache and a logger only.
        var assignment = Pending(TenantA);
        var repo = new Mock<IAssignmentRepository>();
        repo.Setup(r => r.GetAsync(assignment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);

        var handler = new AutoReturnAssignmentCommandHandler(
            repo.Object, NewCache(), NullLogger<AutoReturnAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new AutoReturnAssignmentCommand(
            assignment.Id, ApprovalSweeper.SystemApproverId, ApprovalSweeper.WindowExpiredReason));

        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Rejected);
        assignment.ApprovalReason.Should().Be(ApprovalSweeper.WindowExpiredReason);
        assignment.SubmittedAt.Should().BeNull();
        repo.Verify(r => r.UpdateAsync(assignment, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AutoReturnCommand_OnARowThatIsNoLongerPending_Throws_SoTheSweepIsolatesIt()
    {
        var assignment = NewDraft(TenantA); // never submitted — the domain refuses a non-Pending reject
        var repo = new Mock<IAssignmentRepository>();
        repo.Setup(r => r.GetAsync(assignment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);
        var handler = new AutoReturnAssignmentCommandHandler(
            repo.Object, NewCache(), NullLogger<AutoReturnAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() => handler.HandleAsync(
                new AutoReturnAssignmentCommand(assignment.Id, ApprovalSweeper.SystemApproverId, "approval window expired")))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
