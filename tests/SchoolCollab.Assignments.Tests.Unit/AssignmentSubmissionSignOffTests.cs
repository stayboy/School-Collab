using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Tests.Unit;

[TestClass]
public class AssignmentSubmissionSignOffTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000007a1");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000007a2");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-0000000007a3");
    private static readonly Guid GuardianId = Guid.Parse("00000000-0000-0000-0000-0000000007a4");

    private static AssignmentSubmission CreateSubmission() =>
        AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);

    // ── MarkAwaitingSignature ────────────────────────────────────────────

    [TestMethod]
    public void MarkAwaitingSignature_FromNone_MovesToAwaiting()
    {
        var submission = CreateSubmission();

        submission.MarkAwaitingSignature();

        submission.SignOffState.Should().Be(SignOffState.AwaitingSignature);
    }

    [TestMethod]
    public void MarkAwaitingSignature_WhenAlreadyAwaiting_Throws()
    {
        var submission = CreateSubmission();
        submission.MarkAwaitingSignature();

        var act = () => submission.MarkAwaitingSignature();

        act.Should().Throw<SubmissionSignOffStateException>();
    }

    [TestMethod]
    public void MarkAwaitingSignature_WhenSigned_Throws()
    {
        var submission = CreateSubmission();
        submission.MarkAwaitingSignature();
        submission.MarkSigned();

        var act = () => submission.MarkAwaitingSignature();

        act.Should().Throw<SubmissionSignOffStateException>();
    }

    // ── MarkSigned ────────────────────────────────────────────────────────

    [TestMethod]
    public void MarkSigned_FromAwaiting_RecordsSignedAt()
    {
        var submission = CreateSubmission();
        submission.MarkAwaitingSignature();

        submission.MarkSigned();

        submission.SignOffState.Should().Be(SignOffState.Signed);
        submission.SignedAt.Should().NotBeNull();
    }

    [TestMethod]
    public void MarkSigned_WhenNone_Throws()
    {
        var submission = CreateSubmission();

        var act = () => submission.MarkSigned();

        act.Should().Throw<SubmissionSignOffStateException>();
    }

    [TestMethod]
    public void MarkSigned_WhenAlreadySigned_Throws()
    {
        var submission = CreateSubmission();
        submission.MarkAwaitingSignature();
        submission.MarkSigned();

        var act = () => submission.MarkSigned();

        act.Should().Throw<SubmissionSignOffStateException>();
    }

    // ── FinalizeSignOff ───────────────────────────────────────────────────

    [TestMethod]
    public void FinalizeSignOff_FromSigned_StampsFinalizedAt()
    {
        var submission = CreateSubmission();
        submission.MarkAwaitingSignature();
        submission.MarkSigned();

        submission.FinalizeSignOff();

        submission.FinalizedAt.Should().NotBeNull();
        submission.SignOffState.Should().Be(SignOffState.Signed);
    }

    [TestMethod]
    public void FinalizeSignOff_WhenNotSigned_Throws()
    {
        var submission = CreateSubmission();
        submission.MarkAwaitingSignature();

        var act = () => submission.FinalizeSignOff();

        act.Should().Throw<SubmissionSignOffStateException>();
    }

    [TestMethod]
    public void FinalizeSignOff_WhenNone_Throws()
    {
        var submission = CreateSubmission();

        var act = () => submission.FinalizeSignOff();

        act.Should().Throw<SubmissionSignOffStateException>();
    }

    // ── ReassignSigner ────────────────────────────────────────────────────

    [TestMethod]
    public void ReassignSigner_FromAwaiting_SetsExpectedSigner()
    {
        var submission = CreateSubmission();
        submission.MarkAwaitingSignature();

        submission.ReassignSigner(GuardianId);

        submission.ExpectedSignerGuardianId.Should().Be(GuardianId);
    }

    [TestMethod]
    public void ReassignSigner_WhenNone_Throws()
    {
        var submission = CreateSubmission();

        var act = () => submission.ReassignSigner(GuardianId);

        act.Should().Throw<SubmissionSignOffStateException>();
    }

    [TestMethod]
    public void ReassignSigner_WhenSigned_Throws()
    {
        var submission = CreateSubmission();
        submission.MarkAwaitingSignature();
        submission.MarkSigned();

        var act = () => submission.ReassignSigner(GuardianId);

        act.Should().Throw<SubmissionSignOffStateException>();
    }

    [TestMethod]
    public void ReassignSigner_EmptyGuardian_Throws()
    {
        var submission = CreateSubmission();
        submission.MarkAwaitingSignature();

        var act = () => submission.ReassignSigner(Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("guardianId");
    }

    // ── RecordSubmission lock guard (C4) ──────────────────────────────────

    private static void RecordSubmission(AssignmentSubmission submission) =>
        submission.RecordSubmission(1, SubmissionSource.Student, null, DateTimeOffset.UtcNow);

    [TestMethod]
    public void RecordSubmission_AllowedWhenNone()
    {
        var submission = CreateSubmission();

        RecordSubmission(submission);

        submission.CurrentVersionNumber.Should().Be(1);
    }

    [TestMethod]
    public void RecordSubmission_BlockedWhenSigned()
    {
        var submission = CreateSubmission();
        submission.MarkAwaitingSignature();
        submission.MarkSigned();

        var act = () => RecordSubmission(submission);

        act.Should().Throw<SubmissionLockedException>();
    }

    [TestMethod]
    public void RecordSubmission_BlockedWhenFinalized()
    {
        var submission = CreateSubmission();
        submission.MarkAwaitingSignature();
        submission.MarkSigned();
        submission.FinalizeSignOff();

        var act = () => RecordSubmission(submission);

        act.Should().Throw<SubmissionLockedException>();
    }
}