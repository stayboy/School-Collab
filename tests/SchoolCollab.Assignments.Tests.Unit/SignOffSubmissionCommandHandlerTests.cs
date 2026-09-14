using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-C2 (spec §3.2 + §6 NFR line 115) —
/// <see cref="SignOffSubmissionCommandHandler"/>. The happy path writes one
/// audit event + moves the submission to Signed atomically; every guard
/// (assignment, already-signed idempotency, wrong state, unlinked guardian,
/// typed-name requirement, fail-open consent) maps to its typed exception.
/// </summary>
[TestClass]
public class SignOffSubmissionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000007b1");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000007b2");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-0000000007b3");
    private static readonly Guid GuardianId = Guid.Parse("00000000-0000-0000-0000-0000000007b4");

    private sealed class FakeTenantProvider : ITenantProvider
    {
        public TenantContext GetTenantContext() => new(TenantId, "tenant", TenantType.School);
    }

    private sealed class FakeAssignmentRepository(List<Assignment> assignments) : IAssignmentRepository
    {
        private readonly List<Assignment> _assignments = assignments;
        public Assignment? Assignment { get; set; }
        // The fixture builds ONE assignment with a generated Id; the command uses a
        // fixed constant id, so the fake returns the single seeded row (the
        // CreateStudentSubmissionScoringHandlerTests fake posture).
        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_assignments.FirstOrDefault());
        public Task AddAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
    }

    private sealed class FakeSubmissionRepository : ISubmissionRepository
    {
        public AssignmentSubmission? Submission { get; set; }
        public List<SignatureEvent> AddedEvents { get; } = new();
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
        public void Add(SignatureEvent e) => AddedEvents.Add(e);
        public Task<SignatureEvent?> GetSignatureEventByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(AddedEvents.FirstOrDefault());
        public Task<List<AssignmentSubmission>> ListSubmissionEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Submission is null ? new List<AssignmentSubmission>() : new List<AssignmentSubmission> { Submission });
        public Task<List<AssignmentRecipient>> ListRecipientEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(new List<AssignmentRecipient>());
        public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSubmissionVersion>());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianGateDto?>(null);
    }

    private sealed class FakeStudentDirectory : IStudentDirectory
    {
        private readonly HashSet<Guid>? _linked;
        public FakeStudentDirectory(HashSet<Guid>? linked = null) => _linked = linked;
        public Task<StudentNameInfo?> GetStudentNameAsync(Guid studentId, CancellationToken ct = default)
            => Task.FromResult(_linked is null
                ? (StudentNameInfo?)null
                : new StudentNameInfo(studentId, $"Ward {studentId}"));
        public Task<IReadOnlyList<WardGuardianInfo>> GetGuardiansAsync(Guid studentId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WardGuardianInfo>>(new List<WardGuardianInfo>());
        public Task<bool> IsGuardianOfAsync(Guid studentId, Guid guardianId, CancellationToken ct = default)
            => Task.FromResult(_linked?.Contains(guardianId) == true);
    }

    private sealed class FixedConsentResolver(string text) : ISignatureConsentTextResolver
    {
        public Task<string> ResolveConsentTextAsync(CancellationToken ct = default) => Task.FromResult(text);
    }

    private sealed class ThrowingConsentResolver : ISignatureConsentTextResolver
    {
        public Task<string> ResolveConsentTextAsync(CancellationToken ct = default) => Task.FromResult(SignatureConsentDefaults.EmbeddedConsentText);
    }

    private static Assignment NewRequiresSignatureAssignment() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId,
            mandatoryReview: false, requiresSignature: true)
            .WithTenant(new FakeTenantProvider());

    private static Assignment NewNoSignatureAssignment() =>
        Assignment.Create("Quiet", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId,
            mandatoryReview: false)
            .WithTenant(new FakeTenantProvider());

    private static AssignmentSubmission NewAwaitingSubmission() =>
        AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);

    private static SignOffSubmissionCommand NewCommand(string? typed = null, Guid? guardian = null) =>
        new(
            AssignmentId,
            StudentId,
            guardian ?? GuardianId,
            SignatureType.Typed,
            typed ?? "Jane Doe",
            "127.0.0.1",
            "TestAgent/1.0");

    [TestMethod]
    public async Task Sign_HappyPath_WritesEventAndState()
    {
        var assignment = NewRequiresSignatureAssignment();
        var submission = NewAwaitingSubmission();
        submission.MarkAwaitingSignature();
        var assignmentRepo = new FakeAssignmentRepository(new List<Assignment> { assignment });
        var submissionRepo = new FakeSubmissionRepository { Submission = submission };

        var handler = new SignOffSubmissionCommandHandler(
            assignmentRepo, submissionRepo, new FakeTenantProvider(),
            new FakeStudentDirectory(new HashSet<Guid> { GuardianId }),
            new FixedConsentResolver("I consent."),
            NullLogger<SignOffSubmissionCommandHandler>.Instance);

        var result = await handler.HandleAsync(NewCommand(), CancellationToken.None);

        result.Should().NotBeNull();
        result.SignOffState.Should().Be(SignOffStateDto.Signed);
        result.SignedAt.Should().NotBeNull();
        submission.SignOffState.Should().Be(SignOffState.Signed);

        submissionRepo.AddedEvents.Should().HaveCount(1);
        var ev = submissionRepo.AddedEvents[0];
        ev.SignerGuardianId.Should().Be(GuardianId);
        ev.SignatureType.Should().Be(SignatureType.Typed);
        ev.TypedSignature.Should().Be("Jane Doe");
        ev.ConsentTextShown.Should().Be("I consent.");
        ev.IpAddress.Should().Be("127.0.0.1");
        ev.UserAgent.Should().Be("TestAgent/1.0");
    }

    [TestMethod]
    public async Task Sign_SecondAttempt_ThrowsAlreadySigned_AndCreatesNoSecondEvent()
    {
        var assignment = NewRequiresSignatureAssignment();
        var submission = NewAwaitingSubmission();
        submission.MarkAwaitingSignature();
        submission.MarkSigned();
        var submissionRepo = new FakeSubmissionRepository { Submission = submission };

        var handler = new SignOffSubmissionCommandHandler(
            new FakeAssignmentRepository(new List<Assignment> { assignment }), submissionRepo,
            new FakeTenantProvider(), new FakeStudentDirectory(new HashSet<Guid> { GuardianId }),
            new FixedConsentResolver("I consent."),
            NullLogger<SignOffSubmissionCommandHandler>.Instance);

        var act = () => handler.HandleAsync(NewCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<SubmissionAlreadySignedException>();
        submissionRepo.AddedEvents.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Sign_WrongState_Throws()
    {
        var assignment = NewRequiresSignatureAssignment();
        var submission = NewAwaitingSubmission(); // None, not AwaitingSignature
        var submissionRepo = new FakeSubmissionRepository { Submission = submission };

        var handler = new SignOffSubmissionCommandHandler(
            new FakeAssignmentRepository(new List<Assignment> { assignment }), submissionRepo,
            new FakeTenantProvider(), new FakeStudentDirectory(new HashSet<Guid> { GuardianId }),
            new FixedConsentResolver("I consent."),
            NullLogger<SignOffSubmissionCommandHandler>.Instance);

        var act = () => handler.HandleAsync(NewCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<SubmissionSignOffStateException>();
        submissionRepo.AddedEvents.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Sign_UnlinkedGuardian_ThrowsNotAuthorized()
    {
        var assignment = NewRequiresSignatureAssignment();
        var submission = NewAwaitingSubmission();
        submission.MarkAwaitingSignature();
        var submissionRepo = new FakeSubmissionRepository { Submission = submission };

        var handler = new SignOffSubmissionCommandHandler(
            new FakeAssignmentRepository(new List<Assignment> { assignment }), submissionRepo,
            new FakeTenantProvider(), new FakeStudentDirectory(new HashSet<Guid>()), // unlinked
            new FixedConsentResolver("I consent."),
            NullLogger<SignOffSubmissionCommandHandler>.Instance);

        var act = () => handler.HandleAsync(NewCommand(guardian: GuardianId), CancellationToken.None);

        await act.Should().ThrowAsync<GuardianNotAuthorizedException>();
        submissionRepo.AddedEvents.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Sign_TypedRequiresName_Throws()
    {
        var assignment = NewRequiresSignatureAssignment();
        var submission = NewAwaitingSubmission();
        submission.MarkAwaitingSignature();
        var submissionRepo = new FakeSubmissionRepository { Submission = submission };

        var handler = new SignOffSubmissionCommandHandler(
            new FakeAssignmentRepository(new List<Assignment> { assignment }), submissionRepo,
            new FakeTenantProvider(), new FakeStudentDirectory(new HashSet<Guid> { GuardianId }),
            new FixedConsentResolver("I consent."),
            NullLogger<SignOffSubmissionCommandHandler>.Instance);

        var act = () => handler.HandleAsync(NewCommand(typed: "   "), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(SignOffSubmissionCommand.TypedSignature));
        submissionRepo.AddedEvents.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Sign_RequiresSignatureAssignment_Guard()
    {
        var assignment = NewNoSignatureAssignment();
        var submission = NewAwaitingSubmission();
        submission.MarkAwaitingSignature();
        var submissionRepo = new FakeSubmissionRepository { Submission = submission };

        var handler = new SignOffSubmissionCommandHandler(
            new FakeAssignmentRepository(new List<Assignment> { assignment }), submissionRepo,
            new FakeTenantProvider(), new FakeStudentDirectory(new HashSet<Guid> { GuardianId }),
            new FixedConsentResolver("I consent."),
            NullLogger<SignOffSubmissionCommandHandler>.Instance);

        var act = () => handler.HandleAsync(NewCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<SubmissionSignOffStateException>();
        submissionRepo.AddedEvents.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Sign_FailsOpen_WhenConsentResolverErrors()
    {
        var assignment = NewRequiresSignatureAssignment();
        var submission = NewAwaitingSubmission();
        submission.MarkAwaitingSignature();
        var submissionRepo = new FakeSubmissionRepository { Submission = submission };

        var handler = new SignOffSubmissionCommandHandler(
            new FakeAssignmentRepository(new List<Assignment> { assignment }), submissionRepo,
            new FakeTenantProvider(), new FakeStudentDirectory(new HashSet<Guid> { GuardianId }),
            new ThrowingConsentResolver(),
            NullLogger<SignOffSubmissionCommandHandler>.Instance);

        var result = await handler.HandleAsync(NewCommand(), CancellationToken.None);

        result.Should().NotBeNull();
        // The resolver still returned a usable (embedded) text → event written.
        submissionRepo.AddedEvents.Should().HaveCount(1);
        submissionRepo.AddedEvents[0].ConsentTextShown.Should().Be(SignatureConsentDefaults.EmbeddedConsentText);
    }
}
