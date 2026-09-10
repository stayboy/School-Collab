using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentOnBehalf;

/// <summary>
/// WS-A3 on-behalf handler (spec §4.7 / §4.11). Order mirrors the
/// student-submit path (gate → assignment → cap → validate → score →
/// version-with-Score/Passed → answers → record → save); the
/// feedback envelope is intentionally NOT returned (on-behalf stays
/// void — NoContent surface).
/// </summary>
public sealed class SubmitAssignmentOnBehalfCommandHandler(
    IAssignmentRepository assignmentRepository,
    ISubmissionRepository submissionRepository,
    ITenantProvider tenantProvider,
    IScoringEngine scoringEngine,
    ILogger<SubmitAssignmentOnBehalfCommandHandler> logger) : ICommandHandler<SubmitAssignmentOnBehalfCommand>
{
    public async Task HandleAsync(SubmitAssignmentOnBehalfCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling SubmitAssignmentOnBehalf for assignment {AssignmentId} / student {StudentId}",
            command.AssignmentId, command.StudentId);

        var gate = await submissionRepository.GetGateByAssignmentStudentAsync(command.AssignmentId, command.StudentId, cancellationToken)
            ?? throw new GuardianSubmissionGateNotFoundException(command.AssignmentId, command.StudentId);

        if (!gate.SubmissionEnabledForStudent)
            throw new InvalidOperationException(
                "Guardian submission is not enabled for this student. The Primary guardian must review the gate first.");

        var assignment = await assignmentRepository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var now = DateTimeOffset.UtcNow;

        var submission = await submissionRepository.GetSubmissionByAssignmentStudentAsync(command.AssignmentId, command.StudentId, cancellationToken);

        // WS-A3 / spec §7 Q4: attempt-cap check (mirrors the student path;
        // on-behalf counts as an attempt too).
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

        // WS-A3 / spec §3.3: answer validation.
        SubmissionAnswerValidator.Validate(assignment, command.Answers);

        // WS-A3 / spec §3.3: scoring (TeacherGraded → engine skipped).
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
            var scoreResult = scoringEngine.Score(input);
            score = scoreResult.Score;
            passed = scoreResult.Passed;
        }
        else
        {
            score = null;
            passed = null;
        }

        if (submission is null)
        {
            submission = AssignmentSubmission.Create(tenantId, command.AssignmentId, command.StudentId, gate.Id);
            submissionRepository.Add(submission);
        }

        var newVersion = submission.CurrentVersionNumber + 1;
        var version = AssignmentSubmissionVersion.Create(
            tenantId, submission.Id, command.AssignmentId, command.StudentId,
            newVersion, SubmissionSource.GuardianOnBehalf, command.GuardianId, now, command.Content,
            score: score, passed: passed);
        submissionRepository.Add(version);

        // Per-version answer rows (parity with the student path).
        if (command.Answers is { Count: > 0 })
        {
            foreach (var a in command.Answers)
            {
                submissionRepository.Add(
                    SubmissionAnswer.Create(tenantId, version.Id, a.QuestionId, a.SelectedOptionId, a.TextAnswer));
            }
        }

        submission.RecordSubmission(newVersion, SubmissionSource.GuardianOnBehalf, command.GuardianId, now);
        gate.SubmitOnBehalf(command.GuardianId, command.Content);

        submissionRepository.Update(gate);
        submissionRepository.Update(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Guardian {GuardianId} submitted assignment {AssignmentId} on behalf of student {StudentId} (v{Version}, score={Score}, passed={Passed})",
            command.GuardianId, command.AssignmentId, command.StudentId, newVersion, score, passed);
    }
}
