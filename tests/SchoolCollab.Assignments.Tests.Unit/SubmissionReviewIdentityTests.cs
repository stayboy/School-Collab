using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ApproveAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.OverrideStudentSubmissionAttempts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmission;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// ar-24 AC1 — handler-level discriminating tests for the claim-wins + tenant cross-check on
/// the four submission-review handlers + the review-submission claim-wins override. Built
/// against real domain entities + Moq repository fakes (never a filtered DbContext — the EF
/// tenant filter is bypassed by construction, per plan P2-4). The foreign-tenant case is
/// parameterized over all four handlers; the other cases each pin one discriminating behaviour.
/// </summary>
[TestClass]
public class SubmissionReviewIdentityTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ForeignTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherTeacherId = Guid.Parse("00000000-0000-0000-0000-000000000099");
    private static readonly Guid AssignmentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static FakeCurrentUser SameTenantUser() => new() { CurrentTenant = new TenantContext(TenantId, "same", TenantType.School) };
    private static FakeCurrentUser ForeignTenantUser() => new() { CurrentTenant = new TenantContext(ForeignTenantId, "foreign", TenantType.School) };

    private static Assignment MakeAssignment(Guid tenantId) =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
                TargetAudienceType.AllStudents, Guid.NewGuid(), gradeLevelId: null, null, 100m, TeacherId)
            .WithTenant(tenantId);

    private static AssignmentSubmission MakeSubmission(Guid tenantId) =>
        AssignmentSubmission.Create(tenantId, AssignmentId, StudentId, null);

    private static HybridCache Cache() => new Mock<HybridCache>().Object;

    private static ITenantProvider TenantProvider(Guid tenantId)
    {
        var mock = new Mock<ITenantProvider>();
        mock.Setup(p => p.GetTenantContext()).Returns(new TenantContext(tenantId, "t", TenantType.School));
        return mock.Object;
    }

    private static IAssignmentRepository AssignmentRepo(Assignment assignment)
    {
        var mock = new Mock<IAssignmentRepository>();
        mock.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(assignment);
        mock.Setup(r => r.UpdateAsync(It.IsAny<Assignment>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return mock.Object;
    }

    private static ISubmissionRepository SubmissionRepo(AssignmentSubmission submission)
    {
        var mock = new Mock<ISubmissionRepository>();
        mock.Setup(r => r.GetSubmissionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(submission);
        mock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return mock.Object;
    }

    // ── Case 1: same-tenant claim → allowed (all four handlers succeed) ─────────────

    private static async Task AssertSameTenantAllowed(Func<Task> act)
    {
        await act.Should().NotThrowAsync("a same-tenant acting teacher must be able to review/mutate the entity.");
    }

    [TestMethod]
    public async Task SameTenantClaim_AllowsAllFourHandlers()
    {
        var user = SameTenantUser();
        user.IsAuthenticated = true;
        user.TeacherId = TeacherId;
        var flagOn = new FakeFeatureFlagService { IsEnabledValue = true }; // TestAuth no-claim fallback not used: claim is present

        var assignment = MakeAssignment(TenantId); // CreatedByTeacherId == TeacherId

        // ReviewSubmission
        var submission = MakeSubmission(TenantId);
        var rs = new ReviewSubmissionCommandHandler(
            SubmissionRepo(submission), AssignmentRepo(assignment), TenantProvider(TenantId),
            user, flagOn, NullLogger<ReviewSubmissionCommandHandler>.Instance);
        await AssertSameTenantAllowed(() => rs.HandleAsync(new ReviewSubmissionCommand(submission.Id, TeacherId, 90m, null, "ok"), CancellationToken.None));

        // ReviewAssignment
        var ra = new ReviewAssignmentCommandHandler(
            AssignmentRepo(assignment), Cache(), user, flagOn, NullLogger<ReviewAssignmentCommandHandler>.Instance);
        await AssertSameTenantAllowed(() => ra.HandleAsync(new ReviewAssignmentCommand(assignment.Id, TeacherId, 70m, "ok"), CancellationToken.None));

        // Approve — the domain method requires a Pending assignment (submit-for-approval first).
        assignment.SubmitForApproval();
        var ap = new ApproveAssignmentCommandHandler(
            AssignmentRepo(assignment), Cache(), user, flagOn, NullLogger<ApproveAssignmentCommandHandler>.Instance);
        await AssertSameTenantAllowed(() => ap.HandleAsync(new ApproveAssignmentCommand(assignment.Id, TeacherId), CancellationToken.None));

        // Override
        var ov = new OverrideStudentSubmissionAttemptsCommandHandler(
            SubmissionRepo(submission), Cache(), user, flagOn, NullLogger<OverrideStudentSubmissionAttemptsCommandHandler>.Instance);
        await AssertSameTenantAllowed(() => ov.HandleAsync(new OverrideStudentSubmissionAttemptsCommand(submission.Id, TeacherId), CancellationToken.None));
    }

    // ── Case 2: foreign-tenant claim → TeacherTenantMismatchException (all four) ─────

    [TestMethod]
    public async Task ForeignTenantClaim_RejectedOnAllFourHandlers()
    {
        var user = ForeignTenantUser();
        user.TeacherId = TeacherId;
        var flag = new FakeFeatureFlagService { IsEnabledValue = true };
        var assignment = MakeAssignment(TenantId);
        var submission = MakeSubmission(TenantId);

        var rs = new ReviewSubmissionCommandHandler(
            SubmissionRepo(submission), AssignmentRepo(assignment), TenantProvider(ForeignTenantId),
            user, flag, NullLogger<ReviewSubmissionCommandHandler>.Instance);
        await new Func<Task>(() => rs.HandleAsync(new ReviewSubmissionCommand(submission.Id, TeacherId, 90m, null, "ok"), CancellationToken.None))
            .Should().ThrowAsync<TeacherTenantMismatchException>();

        var ra = new ReviewAssignmentCommandHandler(
            AssignmentRepo(assignment), Cache(), user, flag, NullLogger<ReviewAssignmentCommandHandler>.Instance);
        await new Func<Task>(() => ra.HandleAsync(new ReviewAssignmentCommand(assignment.Id, TeacherId, 70m, "ok"), CancellationToken.None))
            .Should().ThrowAsync<TeacherTenantMismatchException>();

        var ap = new ApproveAssignmentCommandHandler(
            AssignmentRepo(assignment), Cache(), user, flag, NullLogger<ApproveAssignmentCommandHandler>.Instance);
        await new Func<Task>(() => ap.HandleAsync(new ApproveAssignmentCommand(assignment.Id, TeacherId), CancellationToken.None))
            .Should().ThrowAsync<TeacherTenantMismatchException>();

        var ov = new OverrideStudentSubmissionAttemptsCommandHandler(
            SubmissionRepo(submission), Cache(), user, flag, NullLogger<OverrideStudentSubmissionAttemptsCommandHandler>.Instance);
        await new Func<Task>(() => ov.HandleAsync(new OverrideStudentSubmissionAttemptsCommand(submission.Id, TeacherId), CancellationToken.None))
            .Should().ThrowAsync<TeacherTenantMismatchException>();
    }

    // ── Case 3: real-auth + no claim → MissingTeacherPrincipalException ──────────────

    [TestMethod]
    public async Task RealAuth_NoClaim_ThrowsMissingTeacherPrincipal()
    {
        var user = SameTenantUser(); // TeacherId stays null
        var flagOff = new FakeFeatureFlagService { IsEnabledValue = false }; // real-auth (DisableOIDCAuth disabled)
        var assignment = MakeAssignment(TenantId);
        var submission = MakeSubmission(TenantId);

        var rs = new ReviewSubmissionCommandHandler(
            SubmissionRepo(submission), AssignmentRepo(assignment), TenantProvider(TenantId),
            user, flagOff, NullLogger<ReviewSubmissionCommandHandler>.Instance);
        await new Func<Task>(() => rs.HandleAsync(new ReviewSubmissionCommand(submission.Id, TeacherId, 90m, null, "ok"), CancellationToken.None))
            .Should().ThrowAsync<MissingTeacherPrincipalException>();

        var ra = new ReviewAssignmentCommandHandler(
            AssignmentRepo(assignment), Cache(), user, flagOff, NullLogger<ReviewAssignmentCommandHandler>.Instance);
        await new Func<Task>(() => ra.HandleAsync(new ReviewAssignmentCommand(assignment.Id, TeacherId, 70m, "ok"), CancellationToken.None))
            .Should().ThrowAsync<MissingTeacherPrincipalException>();
    }

    // ── Case 4: TestAuth + no claim → wire field honored (R5) ──────────────────────

    [TestMethod]
    public async Task TestAuth_NoClaim_HonorsWireField()
    {
        var user = SameTenantUser(); // TeacherId null
        var flagOn = new FakeFeatureFlagService { IsEnabledValue = true }; // TestAuth/dev
        var assignment = MakeAssignment(TenantId);
        var submission = MakeSubmission(TenantId);

        // The command carries TeacherId (the wire field) and it must be honored (no claim).
        var rs = new ReviewSubmissionCommandHandler(
            SubmissionRepo(submission), AssignmentRepo(assignment), TenantProvider(TenantId),
            user, flagOn, NullLogger<ReviewSubmissionCommandHandler>.Instance);
        await new Func<Task>(() =>
            rs.HandleAsync(new ReviewSubmissionCommand(submission.Id, TeacherId, 90m, null, "ok"), CancellationToken.None))
            .Should().NotThrowAsync("in TestAuth/dev the wire TeacherId field is honored (R5).");

        var ra = new ReviewAssignmentCommandHandler(
            AssignmentRepo(assignment), Cache(), user, flagOn, NullLogger<ReviewAssignmentCommandHandler>.Instance);
        await new Func<Task>(() =>
            ra.HandleAsync(new ReviewAssignmentCommand(assignment.Id, TeacherId, 70m, "ok"), CancellationToken.None))
            .Should().NotThrowAsync("in TestAuth/dev the wire TeacherId field is honored (R5).");
    }

    // ── Case 5: review-submission claim-wins overrides the body id ──────────────────

    [TestMethod]
    public async Task ReviewSubmission_ClaimWinsOverridesBodyId()
    {
        // The claim names the creating teacher; the BODY names a different teacher. Claim-wins
        // must resolve the acting teacher to the creator so the ownership check passes (the
        // pre-ar-24 handler would have honored the body id and thrown UnauthorizedAccessException).
        var user = SameTenantUser();
        user.TeacherId = TeacherId; // == CreatedByTeacherId
        var flagOn = new FakeFeatureFlagService { IsEnabledValue = true };
        var assignment = MakeAssignment(TenantId);
        var submission = MakeSubmission(TenantId);

        var rs = new ReviewSubmissionCommandHandler(
            SubmissionRepo(submission), AssignmentRepo(assignment), TenantProvider(TenantId),
            user, flagOn, NullLogger<ReviewSubmissionCommandHandler>.Instance);

        await new Func<Task>(() =>
            rs.HandleAsync(new ReviewSubmissionCommand(submission.Id, OtherTeacherId, 90m, null, "ok"), CancellationToken.None))
            .Should().NotThrowAsync("the principal's teacher_id claim must override the body TeacherId (ar-24 claim-wins).");
    }
}
