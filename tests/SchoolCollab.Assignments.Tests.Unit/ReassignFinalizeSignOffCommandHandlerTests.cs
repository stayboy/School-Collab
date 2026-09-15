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
using SchoolCollab.Core.Tenancy;
using Moq;

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
/// WS-C1/C4 + C3 — <see cref="FinalizeSignOffCommandHandler"/>: stamps
/// FinalizedAt on a Signed submission (Signed-only guard; the terminal locked
/// marker) and, on success, generates + stores the certificate PDF and attaches
/// the storage path on the audit <see cref="SignatureEvent"/>.
/// </summary>
[TestClass]
public class FinalizeSignOffCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000007d1");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000007d2");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-0000000007d3");
    private static readonly Guid GuardianId = Guid.Parse("00000000-0000-0000-0000-0000000007d4");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-0000000007d5");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-0000000007d6");

    private sealed class FakeSubmissionRepository(AssignmentSubmission? submission, SignatureEvent? signatureEvent = null) : ISubmissionRepository
    {
        public int SaveCalls { get; private set; }
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
        public Task<SignatureEvent?> GetSignatureEventByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(signatureEvent);
        public Task<List<AssignmentSubmission>> ListSubmissionEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSubmission>());
        public Task<List<AssignmentRecipient>> ListRecipientEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(new List<AssignmentRecipient>());
        public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSubmissionVersion>());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) { SaveCalls++; return Task.FromResult(1); }
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

    private static Assignment NewAssignment() =>
        Assignment.Create("Math HW", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId)
            .WithTenant(TenantId);

    private static SignatureEvent NewSignatureEvent() =>
        SignatureEvent.Create(TenantId, AssignmentId, StudentId, GuardianId,
            SignatureType.Typed, "Jane Doe", "192.168.1.10", "Mozilla/5.0 (Test)", "By signing I consent.");

    private static byte[] SamplePdf() => [0x25, 0x50, 0x44, 0x46]; // "%PDF"

    /// <summary>Builds the handler with Moq stubs for the C3 deps; the provided
    /// fakes are the ones each test asserts on (repo, store, generator). The
    /// directory resolves the student display name + the signer guardian.</summary>
    private static FinalizeSignOffCommandHandler BuildHandler(
        FakeSubmissionRepository repo,
        Mock<IAssignmentCertificateGenerator>? generator = null,
        Mock<IFileStore>? store = null,
        IStudentDirectory? directory = null)
    {
        var assignmentRepo = new Mock<IAssignmentRepository>();
        assignmentRepo.Setup(r => r.GetAsync(AssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewAssignment());

        directory ??= new Mock<IStudentDirectory>().Object;

        var consent = new Mock<ISignatureConsentTextResolver>();
        consent.Setup(r => r.ResolveConsentTextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("By signing I consent.");

        var gen = generator ?? new Mock<IAssignmentCertificateGenerator>();
        if (generator is null)
        {
            gen.Setup(g => g.GenerateAsync(It.IsAny<AssignmentCertificateContent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SamplePdf());
        }

        var fs = store ?? new Mock<IFileStore>();
        if (store is null)
        {
            fs.Setup(f => f.StoreAsync(It.IsAny<Stream>(), "certificate.pdf", "application/pdf", It.IsAny<CancellationToken>()))
                .ReturnsAsync("uploads/signoff/cert.pdf");
        }

        return new FinalizeSignOffCommandHandler(
            repo, assignmentRepo.Object, directory,
            consent.Object, gen.Object, fs.Object,
            NullLogger<FinalizeSignOffCommandHandler>.Instance);
    }

    private static Mock<IStudentDirectory> DirectoryWithGuardian()
    {
        var directory = new Mock<IStudentDirectory>();
        directory.Setup(d => d.GetStudentNameAsync(StudentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StudentNameInfo(StudentId, "Ward One"));
        directory.Setup(d => d.GetGuardiansAsync(StudentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WardGuardianInfo> { new(GuardianId, "Jane Doe", true) });
        return directory;
    }

    [TestMethod]
    public async Task Finalize_FromSigned_StampFinalizedAt()
    {
        var submission = NewSignedSubmission();
        var repo = new FakeSubmissionRepository(submission, NewSignatureEvent());

        var handler = BuildHandler(repo, directory: DirectoryWithGuardian().Object);

        await handler.HandleAsync(new FinalizeSignOffCommand(AssignmentId, StudentId), CancellationToken.None);

        submission.FinalizedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Finalize_WrongState_Throws()
    {
        var submission = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null); // None
        var repo = new FakeSubmissionRepository(submission, NewSignatureEvent());

        var handler = BuildHandler(repo, directory: DirectoryWithGuardian().Object);

        var act = () => handler.HandleAsync(new FinalizeSignOffCommand(AssignmentId, StudentId), CancellationToken.None);

        await act.Should().ThrowAsync<SubmissionSignOffStateException>();
        submission.FinalizedAt.Should().BeNull();
        repo.SaveCalls.Should().Be(0, "a failed finalize must not hit SaveChanges");
    }

    // ── C3 binding coverage ─────────────────────────────────────────────

    [TestMethod]
    public async Task Finalize_GeneratesAndAttachesCertificate()
    {
        var submission = NewSignedSubmission();
        var signatureEvent = NewSignatureEvent();
        var repo = new FakeSubmissionRepository(submission, signatureEvent);
        var generator = new Mock<IAssignmentCertificateGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<AssignmentCertificateContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SamplePdf());
        var store = new Mock<IFileStore>();
        store.Setup(f => f.StoreAsync(It.IsAny<Stream>(), "certificate.pdf", "application/pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync("uploads/signoff/cert.pdf");

        var handler = BuildHandler(repo, generator, store, DirectoryWithGuardian().Object);

        await handler.HandleAsync(new FinalizeSignOffCommand(AssignmentId, StudentId), CancellationToken.None);

        submission.FinalizedAt.Should().NotBeNull();
        signatureEvent.CertificateStoragePath.Should().Be("uploads/signoff/cert.pdf",
            "the stored certificate path is attached on the audit event");
        generator.Verify(g => g.GenerateAsync(
                It.Is<AssignmentCertificateContent>(c =>
                    c.AssignmentTitle == "Math HW" && c.StudentName == "Ward One" &&
                    c.SignerGuardianName == "Jane Doe" && c.SignatureType == SignatureType.Typed &&
                    c.TypedSignature == "Jane Doe" && c.ConsentTextShown == "By signing I consent."),
                It.IsAny<CancellationToken>()),
            Times.Once);
        store.Verify(f => f.StoreAsync(It.IsAny<Stream>(), "certificate.pdf", "application/pdf", It.IsAny<CancellationToken>()), Times.Once);
        repo.SaveCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task Finalize_Throws_WhenNoSignatureEvent()
    {
        var submission = NewSignedSubmission();
        var repo = new FakeSubmissionRepository(submission, signatureEvent: null);

        var handler = BuildHandler(repo, directory: DirectoryWithGuardian().Object);

        var act = () => handler.HandleAsync(new FinalizeSignOffCommand(AssignmentId, StudentId), CancellationToken.None);

        await act.Should().ThrowAsync<SubmissionSignOffStateException>();
        submission.FinalizedAt.Should().BeNull();
        repo.SaveCalls.Should().Be(0, "finalize without a prior sign must not persist anything");
    }

    [TestMethod]
    public async Task Finalize_RollsBack_WhenGenerationFails()
    {
        var submission = NewSignedSubmission();
        var signatureEvent = NewSignatureEvent();
        var repo = new FakeSubmissionRepository(submission, signatureEvent);
        var generator = new Mock<IAssignmentCertificateGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<AssignmentCertificateContent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("renderer down"));

        var handler = BuildHandler(repo, generator, directory: DirectoryWithGuardian().Object);

        var act = () => handler.HandleAsync(new FinalizeSignOffCommand(AssignmentId, StudentId), CancellationToken.None);

        (await act.Should().ThrowAsync<AssignmentCertificateException>())
            .Which.InnerException.Should().BeOfType<InvalidOperationException>();
        signatureEvent.CertificateStoragePath.Should().BeNull(
            "generation failure must not attach a certificate path");
        // FinalizeSignOff() stamps FinalizedAt in-memory before generation (plan
        // decision (d) ordering); rollback is at the persistence boundary, so the
        // observable is that SaveChanges is never reached — the staged finalize
        // is not committed and a retry re-finalizes from a Signed submission.
        repo.SaveCalls.Should().Be(0, "a generation failure must not save the finalize");
    }
}
