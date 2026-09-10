using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateStudentSubmission;

/// <summary>
/// WS-A3 student-submit handler (spec §4.7 / §4.11). Order is binding:
/// assignment fetch → gate check → submission fetch → attempt-cap
/// check → answer validation → scoring → version-with-Score/Passed →
/// answers-persisted-per-version → record submission → save.
/// </summary>
public sealed class CreateStudentSubmissionCommandHandler(
    IAssignmentRepository assignmentRepository,
    ISubmissionRepository submissionRepository,
    ITenantProvider tenantProvider,
    IScoringEngine scoringEngine,
    ILogger<CreateStudentSubmissionCommandHandler> logger) : ICommandHandler<CreateStudentSubmissionCommand, SubmissionFeedbackDto?>
{
    public async Task<SubmissionFeedbackDto?> HandleAsync(CreateStudentSubmissionCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CreateStudentSubmission for assignment {AssignmentId} / student {StudentId}",
            command.AssignmentId, command.StudentId);

        var assignment = await assignmentRepository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        var gate = await submissionRepository.GetGateByAssignmentStudentAsync(command.AssignmentId, command.StudentId, cancellationToken);

        // Student self-submit is allowed only when MandatoryReview == false OR the
        // Primary guardian has enabled the gate (spec §4.10).
        if (assignment.MandatoryReview && gate?.SubmissionEnabledForStudent != true)
            throw new UnauthorizedAccessException(
                "Student self-submit is not enabled. The Primary guardian must review the gate first, or the assignment must not require mandatory review.");

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var now = DateTimeOffset.UtcNow;

        // Fetch the existing submission (may be null on first attempt).
        // The cap check runs BEFORE the upsert so nothing persists on
        // throw (the existing upsert position moves down by one step —
        // decision (e)).
        var submission = await submissionRepository.GetSubmissionByAssignmentStudentAsync(command.AssignmentId, command.StudentId, cancellationToken);

        // ── WS-A3 / spec §7 Q4: attempt-cap check ─────────────────────────
        // The cap compares CurrentVersionNumber >= MaxAttempts with
        // AttemptLimitOverriddenAt is null as the override escape; the
        // override is permanent for the submission. A missing submission
        // (first attempt) can never trip a >= 1 cap.
        if (assignment.MaxAttempts is int max
            && submission is not null
            && submission.AttemptLimitOverriddenAt is null
            && submission.CurrentVersionNumber >= max)
        {
            throw new SubmissionAttemptsExhaustedException(
                $"Submission attempts exhausted for assignment {assignment.Id} (max attempts: {max}). " +
                "A teacher may override the cap for this submission.");
        }

        // WS-D plug point: module-progress gating lands here (decision (k)).

        // ── WS-A3 / spec §3.3: answer validation ─────────────────────────
        // First violation → 400 via SubmissionAnswerValidationException.
        // No aggregate mutation runs before this point — a partial
        // submission state can never persist.
        SubmissionAnswerValidator.Validate(assignment, command.Answers);

        // ── WS-A3 / spec §3.3: scoring ────────────────────────────────────
        // The engine is never called for TeacherGraded (handled below);
        // AutoGraded + InstantGraded both score at submit, the difference
        // is feedback exposure (InstantGraded returns the per-question
        // result envelope in the HTTP response).
        ScoringResult? scoreResult = null;
        decimal? score;
        bool? passed;
        if (assignment.GradingFormat != GradingFormat.TeacherGraded)
        {
            var input = new ScoringInput(
                assignment.GradingFormat,
                assignment.MaxScore,
                assignment.PassScore,
                assignment.Questions
                    .Select(q => new ScoringQuestion(q.Id, q.CorrectOptionId, q.ModelAnswer))
                    .ToList(),
                (command.Answers ?? [])
                    .Select(a => new ScoringAnswer(a.QuestionId, a.SelectedOptionId, a.TextAnswer))
                    .ToList());
            scoreResult = scoringEngine.Score(input);
            score = scoreResult.Score;
            passed = scoreResult.Passed;
        }
        else
        {
            score = null;
            passed = null;
        }

        // Upsert the submission (position moves down — after the cap
        // check so nothing persists on a throw).
        if (submission is null)
        {
            submission = AssignmentSubmission.Create(tenantId, command.AssignmentId, command.StudentId, gate?.Id);
            submissionRepository.Add(submission);
        }

        var newVersion = submission.CurrentVersionNumber + 1;
        var version = AssignmentSubmissionVersion.Create(
            tenantId, submission.Id, command.AssignmentId, command.StudentId,
            newVersion, SubmissionSource.Student, null, now, command.Content,
            score: score, passed: passed);
        submissionRepository.Add(version);

        // Per-version answer rows (WS-A3 — analytics read pattern).
        if (command.Answers is { Count: > 0 })
        {
            foreach (var a in command.Answers)
            {
                submissionRepository.Add(
                    SubmissionAnswer.Create(tenantId, version.Id, a.QuestionId, a.SelectedOptionId, a.TextAnswer));
            }
        }

        submission.RecordSubmission(newVersion, SubmissionSource.Student, null, now);
        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Student {StudentId} submitted assignment {AssignmentId} (v{Version}, score={Score}, passed={Passed})",
            command.StudentId, command.AssignmentId, newVersion, score, passed);

        // InstantGraded returns the feedback envelope; AutoGraded /
        // TeacherGraded return null (the route maps null → NoContent).
        if (assignment.GradingFormat == GradingFormat.InstantGraded && scoreResult is not null)
        {
            return new SubmissionFeedbackDto(
                scoreResult.Questions
                    .Select(q => new SubmissionQuestionResultDto(q.QuestionId, q.IsCorrect))
                    .ToList(),
                scoreResult.Score,
                scoreResult.Passed);
        }

        return null;
    }
}
