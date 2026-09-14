using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-C1 (Q5 delegation) — <see cref="ReassignSignOffCommandHandler"/>:
/// records the advisory expected signer (must be a linked guardian; the domain
/// enforces AwaitingSignature-only).
/// </summary>
[TestClass]
public class ReassignSignOffCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000007c1");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000007c2");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-0000000007c3");
    private static readonly Guid NewGuardianId = Guid.Parse("00000000-0000-0000-0000-0000000007c4");

    private sealed class FakeSubmissionRepository(AssignmentSubmission? submission) : ISubmissionRepository
    {
        public AssignmentSubmission? Submission { get; } = submission;
        public List<AssignmentSubmission> Updated { get; } = new();
        public Task<AssignmentRecipient?> GetRecipientAsync(Guid a, Guid c, CancellationToken ct = default) => Task.FromResult<AssignmentRecipient?>(null);
        public void Add(AssignmentRecipient r) { }
        public void Update(AssignmentRecipient r) { }
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public void Add(GuardianSubmissionGate g) { }
        public void Update(GuardianSubmissionGate g) { }
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(new List<GuardianSubmissionGate>());
        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult<AssignmentSubmission?>(null);
        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(Submission);
        public void Add(AssignmentSubmission s) { }
        public void Update(AssignmentSubmission s) => Updated.Add(s);
        public void Add(AssignmentSubmissionVersion v) { }
        public void Add(SubmissionReview r) { }
        public void Add(SubmissionAnswer a) { }
        public void Add(SignatureEvent e) { }
        public Task<SignatureEvent?> GetSignatureEventByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SignatureEvent?>(null);
        public Task<List<AssignmentSubmission>> ListSubmissionEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSubmission>());
        public Task<List<AssignmentRecipient>> ListRecipientEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(new List<AssignmentRecipient>());
        public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSubmissionVersion>());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianGateDto?>(null);
    }

    private sealed class FakeStudentDirectory(HashSet<Guid> linked) : IStudentDirectory
    {
        public Task<StudentNameInfo?> GetStudentNameAsync(Guid studentId, CancellationToken ct = default) => Task.FromResult<StudentNameInfo?>(null);
        public Task<IReadOnlyList<WardGuardianInfo>> GetGuardiansAsync(Guid studentId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WardGuardianInfo>>(new List<WardGuardianInfo>());
        public Task<bool> IsGuardianOfAsync(Guid studentId, Guid guardianId, CancellationToken ct = default) => Task.FromResult(linked.Contains(guardianId));
    }

    private static AssignmentSubmission NewAwaitingSubmission() =>
        AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);

    [TestMethod]
    public async Task Reassign_SetsExpectedSigner()
    {
        var submission = NewAwaitingSubmission();
        submission.MarkAwaitingSignature();
        var repo = new FakeSubmissionRepository(submission);

        var handler = new ReassignSignOffCommandHandler(
            repo, new FakeStudentDirectory(new HashSet<Guid> { NewGuardianId }),
            NullLogger<ReassignSignOffCommandHandler>.Instance);

        await handler.HandleAsync(new ReassignSignOffCommand(AssignmentId, StudentId, NewGuardianId), CancellationToken.None);

        submission.ExpectedSignerGuardianId.Should().Be(NewGuardianId);
        repo.Updated.Should().Contain(submission);
    }

    [TestMethod]
    public async Task Reassign_WrongState_Throws()
    {
        var submission = NewAwaitingSubmission(); // None
        var repo = new FakeSubmissionRepository(submission);

        var handler = new ReassignSignOffCommandHandler(
            repo, new FakeStudentDirectory(new HashSet<Guid> { NewGuardianId }),
            NullLogger<ReassignSignOffCommandHandler>.Instance);

        var act = () => handler.HandleAsync(new ReassignSignOffCommand(AssignmentId, StudentId, NewGuardianId), CancellationToken.None);

        await act.Should().ThrowAsync<SubmissionSignOffStateException>();
    }

    [TestMethod]
    public async Task Reassign_UnlinkedGuardian_Throws()
    {
        var submission = NewAwaitingSubmission();
        submission.MarkAwaitingSignature();
        var repo = new FakeSubmissionRepository(submission);

        var handler = new ReassignSignOffCommandHandler(
            repo, new FakeStudentDirectory(new HashSet<Guid>()), // unlinked
            NullLogger<ReassignSignOffCommandHandler>.Instance);

        var act = () => handler.HandleAsync(new ReassignSignOffCommand(AssignmentId, StudentId, NewGuardianId), CancellationToken.None);

        await act.Should().ThrowAsync<GuardianNotAuthorizedException>();
    }
}

/// <summary>
/// WS-C1/C4 — <see cref="FinalizeSignOffCommandHandler"/>: stamps FinalizedAt
/// on a Signed submission (Signed-only guard; the terminal locked marker).
/// </summary>
[TestClass]
public class FinalizeSignOffCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000007d1");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000007d2");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-0000000007d3");

    private sealed class FakeSubmissionRepository(AssignmentSubmission? submission) : ISubmissionRepository
    {
        public Task<AssignmentRecipient?> GetRecipientAsync(Guid a, Guid c, CancellationToken ct = default) => Task.FromResult<AssignmentRecipient?>(null);
        public void Add(AssignmentRecipient r) { }
        public void Update(AssignmentRecipient r) { }
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public void Add(GuardianSubmissionGate g) { }
        public void Update(GuardianSubmissionGate g) { }
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(new List<GuardianSubmissionGate>());
        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult<AssignmentSubmission?>(null);
        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(submission);
        public void Add(AssignmentSubmission s) { }
        public void Update(AssignmentSubmission s) { }
        public void Add(AssignmentSubmissionVersion v) { }
        public void Add(SubmissionReview r) { }
        public void Add(SubmissionAnswer a) { }
        public void Add(SignatureEvent e) { }
        public Task<SignatureEvent?> GetSignatureEventByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SignatureEvent?>(null);
        public Task<List<AssignmentSubmission>> ListSubmissionEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSubmission>());
        public Task<List<AssignmentRecipient>> ListRecipientEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(new List<AssignmentRecipient>());
        public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSubmissionVersion>());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianGateDto?>(null);
    }

    private static AssignmentSubmission NewSignedSubmission()
    {
        var s = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        s.MarkAwaitingSignature();
        s.MarkSigned();
        return s;
    }

    [TestMethod]
    public async Task Finalize_FromSigned_StampFinalizedAt()
    {
        var submission = NewSignedSubmission();
        var repo = new FakeSubmissionRepository(submission);

        var handler = new FinalizeSignOffCommandHandler(repo, NullLogger<FinalizeSignOffCommandHandler>.Instance);

        await handler.HandleAsync(new FinalizeSignOffCommand(AssignmentId, StudentId), CancellationToken.None);

        submission.FinalizedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Finalize_WrongState_Throws()
    {
        var submission = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null); // None
        var repo = new FakeSubmissionRepository(submission);

        var handler = new FinalizeSignOffCommandHandler(repo, NullLogger<FinalizeSignOffCommandHandler>.Instance);

        var act = () => handler.HandleAsync(new FinalizeSignOffCommand(AssignmentId, StudentId), CancellationToken.None);

        await act.Should().ThrowAsync<SubmissionSignOffStateException>();
        submission.FinalizedAt.Should().BeNull();
    }
}
