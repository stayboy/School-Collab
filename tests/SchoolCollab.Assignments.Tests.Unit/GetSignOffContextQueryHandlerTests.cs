using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-C2 — <see cref="GetSignOffContextQueryHandler"/>: the sign page's single
/// aggregate carrying the review summary, resolved consent text, and linked
/// guardians. Guards: assignment 404, RequiresSignature false, submission 404.
/// </summary>
[TestClass]
public class GetSignOffContextQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000007f1");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000007f2");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-0000000007f3");
    private static readonly Guid GuardianX = Guid.Parse("00000000-0000-0000-0000-0000000007f4");
    private static readonly Guid GuardianY = Guid.Parse("00000000-0000-0000-0000-0000000007f5");

    private sealed class FakeAssignmentRepository(Assignment? assignment) : IAssignmentRepository
    {
        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(assignment);
        public Task AddAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
    }

    private sealed class FakeSubmissionRepository(AssignmentSubmission? submission) : ISubmissionRepository
    {
        public List<AssignmentSubmissionVersion> Versions { get; set; } = new();
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
        public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) => Task.FromResult(Versions);
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianGateDto?>(null);
    }

    private sealed class FakeStudentDirectory(Dictionary<Guid, string> names, Dictionary<Guid, List<WardGuardianInfo>> guardians) : IStudentDirectory
    {
        public Task<StudentNameInfo?> GetStudentNameAsync(Guid studentId, CancellationToken ct = default)
            => Task.FromResult(names.ContainsKey(studentId) ? new StudentNameInfo(studentId, names[studentId]) : null);
        public Task<IReadOnlyList<WardGuardianInfo>> GetGuardiansAsync(Guid studentId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WardGuardianInfo>>(guardians.TryGetValue(studentId, out var g) ? g : new List<WardGuardianInfo>());
        public Task<bool> IsGuardianOfAsync(Guid studentId, Guid guardianId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FixedConsentResolver(string text) : ISignatureConsentTextResolver
    {
        public Task<string> ResolveConsentTextAsync(CancellationToken ct = default) => Task.FromResult(text);
    }

    private static Assignment NewRequiresSignatureAssignment() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, 100m, TeacherId,
            mandatoryReview: false, passScore: 50m, requiresSignature: true);

    private static Assignment NewNoSignatureAssignment() =>
        Assignment.Create("Quiet", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId,
            mandatoryReview: false);

    private static AssignmentSubmission NewSubmission(SignOffState state)
    {
        var s = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        if (state == SignOffState.AwaitingSignature) s.MarkAwaitingSignature();
        if (state == SignOffState.Signed) { s.MarkAwaitingSignature(); s.MarkSigned(); }
        return s;
    }

    private static AssignmentSubmissionVersion NewVersion(int n, decimal? score = null, bool? passed = null) =>
        AssignmentSubmissionVersion.Create(TenantId, Guid.NewGuid(), AssignmentId, StudentId,
            n, SubmissionSource.Student, null, DateTimeOffset.UtcNow, "content", score: score, passed: passed);

    [TestMethod]
    public async Task Aggregate_CarriesSummaryConsentGuardians()
    {
        var assignment = NewRequiresSignatureAssignment();
        var submission = NewSubmission(SignOffState.AwaitingSignature);
        submission.RecordSubmission(2, SubmissionSource.Student, null, DateTimeOffset.UtcNow);
        var directory = new FakeStudentDirectory(
            new() { [StudentId] = "Alice Ward" },
            new() { [StudentId] = new()
                {
                    new WardGuardianInfo(GuardianX, "Xavier", IsPrimary: true),
                    new WardGuardianInfo(GuardianY, "Yolanda", IsPrimary: false),
                } });

        var handler = new GetSignOffContextQueryHandler(
            new FakeAssignmentRepository(assignment),
            new FakeSubmissionRepository(submission) { Versions =
                { NewVersion(1, score: 40m, passed: false), NewVersion(2, score: 60m, passed: true) } },
            directory,
            new FixedConsentResolver("I consent."),
            NullLogger<GetSignOffContextQueryHandler>.Instance);

        var ctx = await handler.HandleAsync(new GetSignOffContextQuery(AssignmentId, StudentId), CancellationToken.None);

        ctx.Should().NotBeNull();
        ctx.AssignmentTitle.Should().Be("Math");
        ctx.StudentName.Should().Be("Alice Ward");
        ctx.SignOffState.Should().Be(SignOffStateDto.AwaitingSignature);
        ctx.CurrentVersionNumber.Should().Be(2);
        ctx.Score.Should().Be(60m);
        ctx.Passed.Should().BeTrue();
        ctx.ConsentText.Should().Be("I consent.");
        ctx.Guardians.Should().HaveCount(2);
        ctx.Guardians.Should().Contain(g => g.GuardianId == GuardianX && g.IsPrimary);
    }

    [TestMethod]
    public async Task Throws_WhenAssignmentMissing()
    {
        var handler = new GetSignOffContextQueryHandler(
            new FakeAssignmentRepository(null),
            new FakeSubmissionRepository(NewSubmission(SignOffState.AwaitingSignature)),
            new FakeStudentDirectory(new(), new()),
            new FixedConsentResolver("I consent."),
            NullLogger<GetSignOffContextQueryHandler>.Instance);

        var act = () => handler.HandleAsync(new GetSignOffContextQuery(AssignmentId, StudentId), CancellationToken.None);

        await act.Should().ThrowAsync<AssignmentNotFoundException>();
    }

    [TestMethod]
    public async Task Throws_WhenRequiresSignatureFalse()
    {
        var handler = new GetSignOffContextQueryHandler(
            new FakeAssignmentRepository(NewNoSignatureAssignment()),
            new FakeSubmissionRepository(NewSubmission(SignOffState.None)),
            new FakeStudentDirectory(new(), new()),
            new FixedConsentResolver("I consent."),
            NullLogger<GetSignOffContextQueryHandler>.Instance);

        var act = () => handler.HandleAsync(new GetSignOffContextQuery(AssignmentId, StudentId), CancellationToken.None);

        await act.Should().ThrowAsync<SubmissionSignOffStateException>();
    }

    [TestMethod]
    public async Task Throws_WhenSubmissionMissing()
    {
        var handler = new GetSignOffContextQueryHandler(
            new FakeAssignmentRepository(NewRequiresSignatureAssignment()),
            new FakeSubmissionRepository(null),
            new FakeStudentDirectory(new(), new()),
            new FixedConsentResolver("I consent."),
            NullLogger<GetSignOffContextQueryHandler>.Instance);

        var act = () => handler.HandleAsync(new GetSignOffContextQuery(AssignmentId, StudentId), CancellationToken.None);

        await act.Should().ThrowAsync<SubmissionNotFoundException>();
    }
}
