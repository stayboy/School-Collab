using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.AssignmentPolicies;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>assignment-approval-workflow</c> (spec §3.1–§3.4, Q10–Q12) — the domain state machine:
/// <list type="bullet">
///   <item><c>SubmitForApproval</c> stamps <c>SubmittedAt</c>, clears the reason, and is legal ONLY
///   from null / Rejected (Pending and Approved are refused so the approval window cannot be reset,
///   Denied is terminal);</item>
///   <item><c>Reject</c> records the optional reason and clears <c>SubmittedAt</c>;</item>
///   <item><c>Deny</c> is terminal and also clears <c>SubmittedAt</c>;</item>
///   <item>the publish / schedule guards stay untouched and refuse Denied without a code change;</item>
///   <item>the shared <see cref="ApprovalWindow"/> arithmetic the read projections and the sweep
///   agree on, including the Q7 "stored non-positive ⇒ no window" rule.</item>
/// </list>
/// </summary>
[TestClass]
public class AssignmentApprovalWorkflowTests
{
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ApproverId = Guid.Parse("00000000-0000-0000-0000-000000000099");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static Assignment Draft() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId);

    private static Assignment Pending()
    {
        var assignment = Draft();
        assignment.SubmitForApproval();
        return assignment;
    }

    // ── SubmitForApproval ───────────────────────────────────────────────

    [TestMethod]
    public void Submit_FromNotSubmitted_StampsSubmittedAt()
    {
        var assignment = Draft();
        var before = DateTimeOffset.UtcNow;

        assignment.SubmitForApproval();

        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Pending);
        assignment.SubmittedAt.Should().NotBeNull();
        assignment.SubmittedAt!.Value.Should().BeOnOrAfter(before);
        assignment.ApprovalReason.Should().BeNull("a fresh submission carries no decision reason");
    }

    [TestMethod]
    public void Submit_FromRejected_IsAllowed_AndClearsThePreviousReason()
    {
        var assignment = Pending();
        assignment.Reject(ApproverId, "Too vague");
        assignment.ApprovalReason.Should().Be("Too vague");

        assignment.SubmitForApproval();

        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Pending);
        assignment.ApprovalReason.Should().BeNull("the author's resubmission supersedes the old reason");
        assignment.SubmittedAt.Should().NotBeNull();
    }

    [TestMethod]
    public void Submit_WhilePending_Throws_SoTheApprovalWindowCannotBeReset()
    {
        var assignment = Pending();
        var originalStamp = assignment.SubmittedAt;

        Action act = () => assignment.SubmitForApproval();

        act.Should().Throw<AssignmentApprovalStateException>()
            .WithMessage("Only an unsubmitted or rejected assignment can be submitted for approval.*");
        assignment.SubmittedAt.Should().Be(originalStamp,
            "a resubmit while pending would re-stamp SubmittedAt and defeat the window (§3.4)");
    }

    [TestMethod]
    public void Submit_FromApproved_Throws()
    {
        var assignment = Pending();
        assignment.Approve(ApproverId);

        Action act = () => assignment.SubmitForApproval();

        act.Should().Throw<AssignmentApprovalStateException>()
            .WithMessage("Only an unsubmitted or rejected assignment can be submitted for approval.*");
        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
    }

    [TestMethod]
    public void Submit_FromDenied_Throws_WithTheTerminalMessage()
    {
        var assignment = Pending();
        assignment.Deny(ApproverId);

        Action act = () => assignment.SubmitForApproval();

        act.Should().Throw<AssignmentApprovalStateException>()
            .WithMessage("A denied assignment cannot be submitted again*");
        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Denied);
    }

    // ── Reject ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Reject_RecordsTheReason_AndClearsTheSubmissionStamp()
    {
        var assignment = Pending();

        assignment.Reject(ApproverId, "Please add the rubric");

        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Rejected);
        assignment.ApprovalReason.Should().Be("Please add the rubric");
        assignment.SubmittedAt.Should().BeNull("a returned row must not keep a stale approval window");
    }

    [TestMethod]
    public void Reject_WithoutAReason_StoresNull()
    {
        var assignment = Pending();

        assignment.Reject(ApproverId);

        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Rejected);
        assignment.ApprovalReason.Should().BeNull();
    }

    [TestMethod]
    public void Reject_WithWhitespaceReason_StoresNull()
    {
        var assignment = Pending();

        assignment.Reject(ApproverId, "   ");

        assignment.ApprovalReason.Should().BeNull("\"no reason\" has exactly one representation");
    }

    [TestMethod]
    public void Reject_TrimsTheReason()
    {
        var assignment = Pending();

        assignment.Reject(ApproverId, "  Needs a rubric  ");

        assignment.ApprovalReason.Should().Be("Needs a rubric");
    }

    [TestMethod]
    public void Reject_ReasonLongerThanTheColumn_Throws400ShapedArgument()
    {
        var assignment = Pending();

        Action act = () => assignment.Reject(ApproverId, new string('x', Assignment.ApprovalReasonMaxLength + 1));

        act.Should().Throw<ArgumentException>();
        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Pending, "a rejected write leaves the row untouched");
    }

    // ── Deny ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Deny_FromPending_IsTerminal_AndClearsTheSubmissionStamp()
    {
        var assignment = Pending();

        assignment.Deny(ApproverId, "Out of scope for this term");

        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Denied);
        assignment.ApprovalReason.Should().Be("Out of scope for this term");
        assignment.SubmittedAt.Should().BeNull();
        assignment.ApprovedBy.Should().BeNull();
        assignment.ApprovedAt.Should().BeNull();
    }

    [TestMethod]
    public void Deny_FromNotSubmitted_Throws()
    {
        var assignment = Draft();

        Action act = () => assignment.Deny(ApproverId);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Only pending assignments can be denied.*");
    }

    [TestMethod]
    public void Deny_EmptyApprover_Throws()
    {
        var assignment = Pending();

        Action act = () => assignment.Deny(Guid.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("approverId");
    }

    // ── The untouched publish / schedule guards ─────────────────────────

    [TestMethod]
    public void Denied_IsRefusedByPublish_WithTheExistingApprovalGuard()
    {
        var assignment = Pending();
        assignment.Deny(ApproverId);

        Action act = () => assignment.Publish(approvalRequired: true);

        act.Should().Throw<AssignmentApprovalRequiredException>(
            "the existing ApprovalStatus != Approved guard already blocks Denied — no guard change was needed");
        assignment.Status.Should().Be(AssignmentStatus.Draft);
    }

    [TestMethod]
    public void Denied_IsRefusedBySchedule_WithTheExistingApprovalGuard()
    {
        var assignment = Pending();
        assignment.Deny(ApproverId);

        Action act = () => assignment.Schedule(DateTimeOffset.UtcNow.AddDays(3), approvalRequired: true);

        act.Should().Throw<AssignmentApprovalRequiredException>();
        assignment.Status.Should().Be(AssignmentStatus.Draft);
    }

    // ── The edit-hold helper ────────────────────────────────────────────

    [TestMethod]
    public void EnsureNotAwaitingApproval_ThrowsOnlyWhilePending()
    {
        var assignment = Draft();
        assignment.EnsureNotAwaitingApproval(); // not submitted ⇒ editable

        assignment.SubmitForApproval();
        Action pending = () => assignment.EnsureNotAwaitingApproval();
        pending.Should().Throw<AssignmentPendingApprovalException>();

        assignment.Reject(ApproverId);
        assignment.EnsureNotAwaitingApproval(); // sent back for redo ⇒ editable again

        assignment.SubmitForApproval();
        assignment.Deny(ApproverId);
        assignment.EnsureNotAwaitingApproval(); // denied ⇒ the hold is released too
    }

    [TestMethod]
    public void AssignmentPendingApprovalException_CarriesTheAssignmentId()
    {
        var assignment = Pending();

        var ex = FluentActions.Invoking(() => assignment.EnsureNotAwaitingApproval())
            .Should().Throw<AssignmentPendingApprovalException>().Which;

        ex.AssignmentId.Should().Be(assignment.Id);
    }

    // ── ApprovalWindow (shared arithmetic) ──────────────────────────────

    [TestMethod]
    public void WindowExpiresAt_IsSubmittedAtPlusTheEffectiveDays()
    {
        var submittedAt = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

        ApprovalWindow.ExpiresAt(submittedAt, 3)
            .Should().Be(submittedAt.AddDays(3));
    }

    [TestMethod]
    public void WindowExpiresAt_IsNull_WhenEitherSideIsMissing()
    {
        ApprovalWindow.ExpiresAt(null, 5).Should().BeNull("nothing was submitted");
        ApprovalWindow.ExpiresAt(DateTimeOffset.UtcNow, null)
            .Should().BeNull("no window configured — the built-in default never invents a deadline");
    }

    [TestMethod]
    public void StoredNonPositiveWindow_IsTreatedAsNoWindow()
    {
        // Q7's consumer-side half (the MaxNotifications posture): the write seams reject a
        // non-positive value, so this only covers a row written around the guard — and it must not
        // make every pending assignment expire immediately.
        ApprovalWindow.EffectiveDays(0).Should().BeNull();
        ApprovalWindow.EffectiveDays(-4).Should().BeNull();
        ApprovalWindow.EffectiveDays(7).Should().Be(7);
        ApprovalWindow.ExpiresAt(DateTimeOffset.UtcNow, 0).Should().BeNull();
    }
}
