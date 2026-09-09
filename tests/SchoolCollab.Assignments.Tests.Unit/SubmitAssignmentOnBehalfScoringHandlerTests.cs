using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentOnBehalf;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-A3 (spec §3.3 + §7 Q4) — <see cref="SubmitAssignmentOnBehalfCommandHandler"/>
/// mirrors the student path: gate → assignment → cap → validate →
/// score → version+answers → save. On-behalf is NoContent (no
/// feedback envelope). The new <c>AssignmentNotFoundException</c> 404
/// path also lives here.
/// </summary>
[TestClass]
public class SubmitAssignmentOnBehalfScoringHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GuardianId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AssignmentId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid Q1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Q2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OptA = Guid.Parse("a1111111-1111-1111-1111-111111111111");

    private sealed class FakeTenantProvider : ITenantProvider
    {
        private readonly TenantContext _ctx;
        public FakeTenantProvider(Guid tenantId) => _ctx = new TenantContext(tenantId, tenantId.ToString(), TenantType.School);
        public TenantContext GetTenantContext() => _ctx;
    }

    private sealed class FakeAssignmentRepository : IAssignmentRepository
    {
        public Assignment? Assignment;
        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Assignment);
        public Task AddAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());
    }

    private sealed class FakeSubmissionRepository : ISubmissionRepository
    {
        public GuardianSubmissionGate? GateToReturn;
        public AssignmentSubmission? SubmissionToReturn;
        public List<GuardianSubmissionGate> UpdatedGates { get; } = new();
        public List<AssignmentSubmission> AddedSubmissions { get; } = new();
        public List<AssignmentSubmission> UpdatedSubmissions { get; } = new();
        public List<AssignmentSubmissionVersion> AddedVersions { get; } = new();
        public List<SubmissionAnswer> AddedAnswers { get; } = new();

        public Task<AssignmentRecipient?> GetRecipientAsync(Guid a, Guid c, CancellationToken ct = default) => Task.FromResult<AssignmentRecipient?>(null);
        public void Add(AssignmentRecipient r) { }
        public void Update(AssignmentRecipient r) { }
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(GateToReturn);
        public void Add(GuardianSubmissionGate g) { }
        public void Update(GuardianSubmissionGate g) => UpdatedGates.Add(g);
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(new List<GuardianSubmissionGate>());
        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult<AssignmentSubmission?>(null);
        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(SubmissionToReturn);
        public void Add(AssignmentSubmission s) => AddedSubmissions.Add(s);
        public void Update(AssignmentSubmission s) => UpdatedSubmissions.Add(s);
        public void Add(AssignmentSubmissionVersion v) => AddedVersions.Add(v);
        public void Add(SubmissionReview r) { }
        public void Add(SubmissionAnswer a) => AddedAnswers.Add(a);
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianGateDto?>(null);
    }

    private sealed class RecordingScoringEngine : IScoringEngine
    {
        public int CallCount;
        public ScoringResult NextResult = new(Array.Empty<ScoringQuestionResult>(), 0m, null);
        public ScoringResult Score(ScoringInput input) { CallCount++; return NextResult; }
    }

    private static void SetId(object entity, Guid id) =>
        entity.GetType().GetProperty("Id")!.SetValue(entity, id);

    private static void SetCorrectOptionId(AssignmentQuestion question, Guid optionId) =>
        typeof(AssignmentQuestion).GetProperty("CorrectOptionId")!.SetValue(question, optionId);

    private static Assignment NewAutoGradedAssignment(int? maxAttempts = null)
    {
        var a = Assignment.Create("Math", null, AssignmentType.Digital,
            GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, 100m, TeacherId,
            passScore: 50m, maxAttempts: maxAttempts)
            .WithTenant(new FakeTenantProvider(TenantId));
        var q = a.AddQuestion("Q1?", QuestionType.MultipleChoice, 0);
        var optA = q.AddOption("A", isCorrect: true);
        q.AddOption("B", isCorrect: false);
        SetId(q, Q1);
        SetId(optA, OptA);
        SetCorrectOptionId(q, OptA);
        return a;
    }

    private static GuardianSubmissionGate EnabledGate()
    {
        var gate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId);
        gate.Review(GuardianId, approve: true, null);
        return gate;
    }

    private static SubmitAssignmentOnBehalfCommandHandler NewHandler(
        Assignment assignment,
        FakeSubmissionRepository subRepo,
        IScoringEngine scoring)
    {
        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        return new SubmitAssignmentOnBehalfCommandHandler(
            assignmentRepo, subRepo, new FakeTenantProvider(TenantId),
            scoring, NullLogger<SubmitAssignmentOnBehalfCommandHandler>.Instance);
    }

    [TestMethod]
    public async Task OnBehalf_WithAnswers_ScoresAndPersists()
    {
        var assignment = NewAutoGradedAssignment();
        var subRepo = new FakeSubmissionRepository { GateToReturn = EnabledGate() };
        var scoring = new RecordingScoringEngine
        {
            NextResult = new ScoringResult(
                [new ScoringQuestionResult(Q1, true)],
                Score: 100m, Passed: true)
        };

        var handler = NewHandler(assignment, subRepo, scoring);
        await handler.HandleAsync(new SubmitAssignmentOnBehalfCommand(
            AssignmentId, StudentId, GuardianId, "ans",
            [new(Q1, OptA, null)]));

        subRepo.AddedVersions.Should().HaveCount(1);
        subRepo.AddedVersions[0].Source.Should().Be(SubmissionSource.GuardianOnBehalf);
        subRepo.AddedVersions[0].Score.Should().Be(100m);
        subRepo.AddedVersions[0].Passed.Should().BeTrue();
        subRepo.AddedAnswers.Should().HaveCount(1);
        subRepo.UpdatedGates.Should().HaveCount(1);
        subRepo.UpdatedGates[0].SubmittedByGuardianId.Should().Be(GuardianId);
        scoring.CallCount.Should().Be(1);
    }

    [TestMethod]
    public async Task OnBehalf_AttemptCap_Throws()
    {
        var assignment = NewAutoGradedAssignment(maxAttempts: 2);
        var existing = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        typeof(AssignmentSubmission).GetProperty("CurrentVersionNumber")!.SetValue(existing, 2);
        var subRepo = new FakeSubmissionRepository { GateToReturn = EnabledGate(), SubmissionToReturn = existing };
        var scoring = new RecordingScoringEngine();

        var handler = NewHandler(assignment, subRepo, scoring);
        var act = async () => await handler.HandleAsync(new SubmitAssignmentOnBehalfCommand(
            AssignmentId, StudentId, GuardianId, "x",
            [new(Q1, OptA, null)]));

        await act.Should().ThrowAsync<SubmissionAttemptsExhaustedException>();
        subRepo.AddedVersions.Should().BeEmpty();
        scoring.CallCount.Should().Be(0);
    }

    [TestMethod]
    public async Task OnBehalf_AnswerValidation_DuplicateQuestionId_Throws()
    {
        var assignment = NewAutoGradedAssignment();
        var subRepo = new FakeSubmissionRepository { GateToReturn = EnabledGate() };
        var scoring = new RecordingScoringEngine();

        var handler = NewHandler(assignment, subRepo, scoring);
        var act = async () => await handler.HandleAsync(new SubmitAssignmentOnBehalfCommand(
            AssignmentId, StudentId, GuardianId, "x",
            [
                new(Q1, OptA, null),
                new(Q1, OptA, null),
            ]));

        await act.Should().ThrowAsync<SubmissionAnswerValidationException>();
    }

    [TestMethod]
    public async Task OnBehalf_AnswerValidation_UnknownQuestionId_Throws()
    {
        var assignment = NewAutoGradedAssignment();
        var subRepo = new FakeSubmissionRepository { GateToReturn = EnabledGate() };
        var scoring = new RecordingScoringEngine();

        var handler = NewHandler(assignment, subRepo, scoring);
        var act = async () => await handler.HandleAsync(new SubmitAssignmentOnBehalfCommand(
            AssignmentId, StudentId, GuardianId, "x",
            [new(Guid.NewGuid(), OptA, null)]));

        await act.Should().ThrowAsync<SubmissionAnswerValidationException>();
    }

    [TestMethod]
    public async Task OnBehalf_UnknownAssignment_ThrowsAssignmentNotFoundException()
    {
        // Gate exists but assignment does not — handler now loads the
        // assignment (WS-A3) and 404s.
        var subRepo = new FakeSubmissionRepository { GateToReturn = EnabledGate() };
        var scoring = new RecordingScoringEngine();

        var handler = NewHandler(assignment: null!, subRepo, scoring);
        var act = async () => await handler.HandleAsync(new SubmitAssignmentOnBehalfCommand(
            AssignmentId, StudentId, GuardianId, "x"));

        await act.Should().ThrowAsync<AssignmentNotFoundException>();
        subRepo.AddedVersions.Should().BeEmpty();
    }
}
