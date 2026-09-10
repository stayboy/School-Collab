using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateStudentSubmission;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-A3 (spec §3.3 + §7 Q4) — <see cref="CreateStudentSubmissionCommandHandler"/>
/// scores the inbound <see cref="SubmissionAnswerDto"/> list via
/// <see cref="IScoringEngine"/>, persists per-version answer rows,
/// and returns the <see cref="SubmissionFeedbackDto"/> only for
/// InstantGraded. Attempt-cap enforcement + override-clear + answer
/// validation + TeacherGraded no-score + gate-pinned behavior all
/// live here (the <c>SubmissionEngineTests</c> handles the legacy
/// non-scoring paths).
/// </summary>
[TestClass]
public class CreateStudentSubmissionScoringHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AssignmentId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid Q1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Q2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OptA = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid OptB = Guid.Parse("b1111111-1111-1111-1111-111111111111");

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
        public Task<AssignmentRecipient?> GetRecipientAsync(Guid a, Guid c, CancellationToken ct = default) => Task.FromResult<AssignmentRecipient?>(null);
        public void Add(AssignmentRecipient r) { }
        public void Update(AssignmentRecipient r) { }
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(GateToReturn);
        public void Add(GuardianSubmissionGate g) { }
        public void Update(GuardianSubmissionGate g) { }
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
            => Task.FromResult(new List<GuardianSubmissionGate>());
        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult<AssignmentSubmission?>(null);
        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(SubmissionToReturn);
        public List<AssignmentSubmission> AddedSubmissions { get; } = new();
        public List<AssignmentSubmission> UpdatedSubmissions { get; } = new();
        public List<AssignmentSubmissionVersion> AddedVersions { get; } = new();
        public List<SubmissionReview> AddedReviews { get; } = new();
        public List<SubmissionAnswer> AddedAnswers { get; } = new();
        public GuardianSubmissionGate? GateToReturn;
        public AssignmentSubmission? SubmissionToReturn;
        public void Add(AssignmentSubmission s) => AddedSubmissions.Add(s);
        public void Update(AssignmentSubmission s) => UpdatedSubmissions.Add(s);
        public void Add(AssignmentSubmissionVersion v) => AddedVersions.Add(v);
        public void Add(SubmissionReview r) => AddedReviews.Add(r);
        public void Add(SubmissionAnswer a) => AddedAnswers.Add(a);
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianGateDto?>(null);
    }

    private sealed class RecordingScoringEngine : IScoringEngine
    {
        public int CallCount;
        public ScoringInput? LastInput;
        public ScoringResult NextResult = new(Array.Empty<ScoringQuestionResult>(), 0m, null);

        public ScoringResult Score(ScoringInput input)
        {
            CallCount++;
            LastInput = input;
            return NextResult;
        }
    }

    private static void SetId(object entity, Guid id) =>
        entity.GetType().GetProperty("Id")!.SetValue(entity, id);

    private static void SetCorrectOptionId(AssignmentQuestion question, Guid optionId) =>
        typeof(AssignmentQuestion).GetProperty("CorrectOptionId")!.SetValue(question, optionId);

    private static Assignment NewAutoGradedAssignment(
        decimal? maxScore = null,
        decimal? passScore = null,
        int? maxAttempts = null)
    {
        var a = Assignment.Create("Math", null, AssignmentType.Digital,
            GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, maxScore, TeacherId,
            mandatoryReview: false, passScore: passScore, maxAttempts: maxAttempts)
            .WithTenant(new FakeTenantProvider(TenantId));
        var q1 = a.AddQuestion("Q1?", QuestionType.MultipleChoice, 0);
        var q1a = q1.AddOption("A", isCorrect: true);
        var q1b = q1.AddOption("B", isCorrect: false);
        SetId(q1, Q1);
        SetId(q1a, OptA);
        SetId(q1b, OptB);
        SetCorrectOptionId(q1, OptA);

        var q2 = a.AddQuestion("Q2?", QuestionType.MultipleChoice, 1);
        var q2a = q2.AddOption("A", isCorrect: true);
        var q2b = q2.AddOption("B", isCorrect: false);
        SetId(q2, Q2);
        SetId(q2a, OptA);
        SetId(q2b, OptB);
        SetCorrectOptionId(q2, OptA);
        return a;
    }

    private static Assignment NewInstantGradedAssignment()
    {
        var a = Assignment.Create("Math", null, AssignmentType.Digital,
            GradingFormat.InstantGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, 100m, TeacherId,
            mandatoryReview: false, passScore: 50m, maxAttempts: null)
            .WithTenant(new FakeTenantProvider(TenantId));
        var q = a.AddQuestion("Q1?", QuestionType.MultipleChoice, 0);
        var optA = q.AddOption("A", isCorrect: true);
        var optB = q.AddOption("B", isCorrect: false);
        SetId(q, Q1);
        SetId(optA, OptA);
        SetId(optB, OptB);
        SetCorrectOptionId(q, OptA);
        return a;
    }

    private static Assignment NewTeacherGradedAssignment()
    {
        var a = Assignment.Create("Math", null, AssignmentType.Digital,
            GradingFormat.TeacherGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, null, TeacherId,
            mandatoryReview: false)
            .WithTenant(new FakeTenantProvider(TenantId));
        a.AddQuestion("Q1?", QuestionType.MultipleChoice, 0);
        return a;
    }

    private static CreateStudentSubmissionCommandHandler NewHandler(
        Assignment assignment,
        FakeSubmissionRepository subRepo,
        IScoringEngine scoring)
    {
        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        return new CreateStudentSubmissionCommandHandler(
            assignmentRepo, subRepo, new FakeTenantProvider(TenantId),
            scoring, NullLogger<CreateStudentSubmissionCommandHandler>.Instance);
    }

    private static IReadOnlyList<SubmissionAnswerDto> SampleAnswers() =>
    [
        new(Q1, OptA, null),
        new(Q2, OptA, null),
    ];

    // ── AutoGraded scoring path ──────────────────────────────────────────

    [TestMethod]
    public async Task AutoGraded_SubmitWithAnswers_VersionCarriesScoreAndPassed_AndAnswersPersisted()
    {
        var assignment = NewAutoGradedAssignment(maxScore: 100m, passScore: 50m);
        var subRepo = new FakeSubmissionRepository();
        var scoring = new RecordingScoringEngine
        {
            NextResult = new ScoringResult(
                [
                    new ScoringQuestionResult(Q1, true),
                    new ScoringQuestionResult(Q2, true),
                ],
                Score: 100m,
                Passed: true)
        };

        var handler = NewHandler(assignment, subRepo, scoring);
        var feedback = await handler.HandleAsync(
            new CreateStudentSubmissionCommand(AssignmentId, StudentId, "answer", SampleAnswers()));

        feedback.Should().BeNull("AutoGraded returns null → route maps to NoContent");
        subRepo.AddedVersions.Should().HaveCount(1);
        var version = subRepo.AddedVersions[0];
        version.Score.Should().Be(100m);
        version.Passed.Should().BeTrue();
        subRepo.AddedAnswers.Should().HaveCount(2);
        subRepo.AddedAnswers.Should().AllSatisfy(a =>
            a.SubmissionVersionId.Should().Be(version.Id, "answers must point at the new version"));
        scoring.CallCount.Should().Be(1, "AutoGraded calls the engine");
    }

    // ── InstantGraded feedback path ──────────────────────────────────────

    [TestMethod]
    public async Task InstantGraded_Submit_ReturnsSubmissionFeedbackDto()
    {
        var assignment = NewInstantGradedAssignment();
        var subRepo = new FakeSubmissionRepository();
        var scoring = new RecordingScoringEngine
        {
            NextResult = new ScoringResult(
                [new ScoringQuestionResult(Q1, true)],
                Score: 100m,
                Passed: true)
        };

        var handler = NewHandler(assignment, subRepo, scoring);
        var feedback = await handler.HandleAsync(
            new CreateStudentSubmissionCommand(AssignmentId, StudentId, "ans", [new(Q1, OptA, null)]));

        feedback.Should().NotBeNull();
        feedback!.Questions.Should().HaveCount(1);
        feedback.Questions[0].QuestionId.Should().Be(Q1);
        feedback.Questions[0].IsCorrect.Should().BeTrue();
        feedback.Score.Should().Be(100m);
        feedback.Passed.Should().BeTrue();
    }

    // ── AutoGraded + TeacherGraded return null ───────────────────────────

    [TestMethod]
    public async Task AutoGraded_Submit_ReturnsNull()
    {
        var assignment = NewAutoGradedAssignment();
        var subRepo = new FakeSubmissionRepository();
        var scoring = new RecordingScoringEngine
        {
            NextResult = new ScoringResult(
                [new ScoringQuestionResult(Q1, true)],
                Score: 50m,
                Passed: false)
        };

        var handler = NewHandler(assignment, subRepo, scoring);
        var feedback = await handler.HandleAsync(
            new CreateStudentSubmissionCommand(AssignmentId, StudentId, "x", [new(Q1, OptA, null)]));

        feedback.Should().BeNull("AutoGraded returns null → route maps to NoContent");
    }

    [TestMethod]
    public async Task TeacherGraded_Submit_VersionScoreNullPassedNull_AndEngineNeverInvoked()
    {
        var assignment = NewTeacherGradedAssignment();
        var subRepo = new FakeSubmissionRepository();
        var scoring = new RecordingScoringEngine();

        var handler = NewHandler(assignment, subRepo, scoring);
        await handler.HandleAsync(new CreateStudentSubmissionCommand(AssignmentId, StudentId, "x"));

        subRepo.AddedVersions.Should().HaveCount(1);
        subRepo.AddedVersions[0].Score.Should().BeNull();
        subRepo.AddedVersions[0].Passed.Should().BeNull();
        scoring.CallCount.Should().Be(0, "TeacherGraded never invokes the scoring engine");
    }

    // ── Attempt-cap enforcement ──────────────────────────────────────────

    [TestMethod]
    public async Task AttemptCapReached_Throws_AndNothingPersists()
    {
        var assignment = NewAutoGradedAssignment(maxAttempts: 2);
        var existing = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        // bump the existing version number to the cap so CurrentVersionNumber >= max (2)
        typeof(AssignmentSubmission).GetProperty("CurrentVersionNumber")!.SetValue(existing, 2);
        var subRepo = new FakeSubmissionRepository { SubmissionToReturn = existing };
        var scoring = new RecordingScoringEngine();

        var handler = NewHandler(assignment, subRepo, scoring);
        var act = async () => await handler.HandleAsync(
            new CreateStudentSubmissionCommand(AssignmentId, StudentId, "x", SampleAnswers()));

        await act.Should().ThrowAsync<SubmissionAttemptsExhaustedException>();
        subRepo.AddedVersions.Should().BeEmpty();
        subRepo.AddedAnswers.Should().BeEmpty();
        subRepo.AddedSubmissions.Should().BeEmpty();
        subRepo.UpdatedSubmissions.Should().BeEmpty();
        scoring.CallCount.Should().Be(0);
    }

    [TestMethod]
    public async Task AttemptCap_Unlimited_Allowed()
    {
        var assignment = NewAutoGradedAssignment(maxAttempts: null);
        var existing = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        typeof(AssignmentSubmission).GetProperty("CurrentVersionNumber")!.SetValue(existing, 99);
        var subRepo = new FakeSubmissionRepository { SubmissionToReturn = existing };
        var scoring = new RecordingScoringEngine
        {
            NextResult = new ScoringResult(
                [new ScoringQuestionResult(Q1, true), new ScoringQuestionResult(Q2, true)],
                Score: 100m, Passed: true)
        };

        var handler = NewHandler(assignment, subRepo, scoring);
        var feedback = await handler.HandleAsync(
            new CreateStudentSubmissionCommand(AssignmentId, StudentId, "x", SampleAnswers()));

        feedback.Should().BeNull("AutoGraded");
        subRepo.AddedVersions.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task AttemptCap_BelowCap_Allowed()
    {
        var assignment = NewAutoGradedAssignment(maxAttempts: 2);
        var existing = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        typeof(AssignmentSubmission).GetProperty("CurrentVersionNumber")!.SetValue(existing, 1);
        var subRepo = new FakeSubmissionRepository { SubmissionToReturn = existing };
        var scoring = new RecordingScoringEngine
        {
            NextResult = new ScoringResult(
                [new ScoringQuestionResult(Q1, true), new ScoringQuestionResult(Q2, true)],
                Score: 100m, Passed: true)
        };

        var handler = NewHandler(assignment, subRepo, scoring);
        await handler.HandleAsync(
            new CreateStudentSubmissionCommand(AssignmentId, StudentId, "x", SampleAnswers()));

        subRepo.AddedVersions.Should().HaveCount(1);
    }

    // ── Override clears the cap ─────────────────────────────────────────

    [TestMethod]
    public async Task AttemptCap_WithOverride_AllowsSubmitPastCap()
    {
        var assignment = NewAutoGradedAssignment(maxAttempts: 2);
        var existing = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        typeof(AssignmentSubmission).GetProperty("CurrentVersionNumber")!.SetValue(existing, 2);
        existing.OverrideAttemptLimit(Guid.NewGuid()); // teacher previously overrode
        var subRepo = new FakeSubmissionRepository { SubmissionToReturn = existing };
        var scoring = new RecordingScoringEngine
        {
            NextResult = new ScoringResult(
                [new ScoringQuestionResult(Q1, true), new ScoringQuestionResult(Q2, true)],
                Score: 100m, Passed: true)
        };

        var handler = NewHandler(assignment, subRepo, scoring);
        await handler.HandleAsync(
            new CreateStudentSubmissionCommand(AssignmentId, StudentId, "x", SampleAnswers()));

        subRepo.AddedVersions.Should().HaveCount(1, "override cleared the cap");
    }

    // ── Answer validation ────────────────────────────────────────────────

    [TestMethod]
    public async Task AnswerValidation_UnknownQuestionId_Throws_AndNothingPersists()
    {
        var assignment = NewAutoGradedAssignment();
        var subRepo = new FakeSubmissionRepository();
        var scoring = new RecordingScoringEngine();

        var handler = NewHandler(assignment, subRepo, scoring);
        var act = async () => await handler.HandleAsync(new CreateStudentSubmissionCommand(
            AssignmentId, StudentId, "x",
            [new SubmissionAnswerDto(Guid.NewGuid(), OptA, null)]));

        await act.Should().ThrowAsync<SubmissionAnswerValidationException>();
        subRepo.AddedVersions.Should().BeEmpty();
        subRepo.AddedAnswers.Should().BeEmpty();
        scoring.CallCount.Should().Be(0);
    }

    [TestMethod]
    public async Task AnswerValidation_SelectedOptionNotOnQuestion_Throws()
    {
        var assignment = NewAutoGradedAssignment();
        var subRepo = new FakeSubmissionRepository();
        var scoring = new RecordingScoringEngine();

        var handler = NewHandler(assignment, subRepo, scoring);
        var act = async () => await handler.HandleAsync(new CreateStudentSubmissionCommand(
            AssignmentId, StudentId, "x",
            [new SubmissionAnswerDto(Q1, Guid.NewGuid(), null), new SubmissionAnswerDto(Q2, OptA, null)]));

        await act.Should().ThrowAsync<SubmissionAnswerValidationException>();
        subRepo.AddedVersions.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AnswerValidation_SelectedOptionNotAQuestionOption_Throws()
    {
        var assignment = NewAutoGradedAssignment();
        var subRepo = new FakeSubmissionRepository();
        var scoring = new RecordingScoringEngine();
        var bogusOption = Guid.NewGuid();

        var handler = NewHandler(assignment, subRepo, scoring);
        var act = async () => await handler.HandleAsync(new CreateStudentSubmissionCommand(
            AssignmentId, StudentId, "x",
            [new SubmissionAnswerDto(Q1, bogusOption, null)]));

        await act.Should().ThrowAsync<SubmissionAnswerValidationException>();
        subRepo.AddedVersions.Should().BeEmpty();
        subRepo.AddedAnswers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AnswerValidation_DuplicateQuestionId_Throws()
    {
        var assignment = NewAutoGradedAssignment();
        var subRepo = new FakeSubmissionRepository();
        var scoring = new RecordingScoringEngine();

        var handler = NewHandler(assignment, subRepo, scoring);
        var act = async () => await handler.HandleAsync(new CreateStudentSubmissionCommand(
            AssignmentId, StudentId, "x",
            [
                new SubmissionAnswerDto(Q1, OptA, null),
                new SubmissionAnswerDto(Q1, OptA, null),
            ]));

        await act.Should().ThrowAsync<SubmissionAnswerValidationException>();
        subRepo.AddedVersions.Should().BeEmpty();
    }

    // ── Gate check still enforced ────────────────────────────────────────

    [TestMethod]
    public async Task GateCheck_StillEnforced_UnauthorizedAccessException()
    {
        var assignment = Assignment.Create("Math", null, AssignmentType.Digital,
            GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, null, TeacherId)
            .WithTenant(new FakeTenantProvider(TenantId));
        // assignment has no questions, but the gate check fires first
        var gate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId); // not reviewed → disabled
        var subRepo = new FakeSubmissionRepository { GateToReturn = gate };
        var scoring = new RecordingScoringEngine();

        var handler = NewHandler(assignment, subRepo, scoring);
        var act = async () => await handler.HandleAsync(new CreateStudentSubmissionCommand(AssignmentId, StudentId, "x"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
