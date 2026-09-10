using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CloseAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateStudentSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DeleteAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmissionGate;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentOnBehalf;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UnpublishAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetGuardianGate;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmissionsForReview;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentRecipients;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListSubmissionsByAssignment;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.EnableStudentSubmission;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Api.Endpoints;

public static class AssignmentRoutes
{
    public static RouteGroupBuilder MapAssignmentRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            [FromQuery] AssignmentStatus? status,
            [FromServices] IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]> handler,
            CancellationToken ct) =>
        {
            var query = new ListAssignmentsQuery(status);
            var results = await handler.HandleAsync(query, ct);
            return Results.Ok(results);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetAssignmentByIdQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/", async (
            [FromBody] CreateAssignmentRequest req,
            [FromServices] ICommandHandler<CreateAssignmentCommand, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var cmd = new CreateAssignmentCommand(
                    req.Title, req.Description, (AssignmentType)req.AssignmentType,
                    (GradingFormat)req.GradingFormat, (TargetAudienceType)req.TargetAudienceType,
                    req.TopicId, req.GradeLevelId,
                    req.DueDate, req.MaxScore,
                    req.MandatoryReview,
                    req.AiPromptOverride,
                    req.Questions,
                    req.Attachments);
                var id = await handler.HandleAsync(cmd, ct);
                return Results.Created($"/assignments/{id}", new { id });
            }
            catch (AssignmentQuestionValidationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateAssignmentRequest req,
            [FromServices] ICommandHandler<UpdateAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                var cmd = new UpdateAssignmentCommand(
                    id, req.Title, req.Description, (AssignmentType)req.AssignmentType,
                    (GradingFormat)req.GradingFormat, (TargetAudienceType)req.TargetAudienceType,
                    req.TopicId, req.GradeLevelId,
                    req.DueDate, req.MaxScore, req.MandatoryReview,
                    req.AiPromptOverride,
                    req.Questions,
                    req.Attachments);
                await handler.HandleAsync(cmd, ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (AssignmentQuestionValidationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] ICommandHandler<DeleteAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteAssignmentCommand(id), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        group.MapPost("/{id:guid}/publish", async (
            Guid id,
            [FromBody] PublishAssignmentRequest? req,
            [FromServices] ICommandHandler<PublishAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new PublishAssignmentCommand(id, req?.ContactIds), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/{id:guid}/unpublish", async (
            Guid id,
            [FromServices] ICommandHandler<UnpublishAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UnpublishAssignmentCommand(id), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        group.MapPost("/{id:guid}/close", async (
            Guid id,
            [FromServices] ICommandHandler<CloseAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new CloseAssignmentCommand(id), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/{id:guid}/review", async (
            Guid id,
            [FromBody] ReviewAssignmentRequest req,
            [FromServices] ICommandHandler<ReviewAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                var cmd = new ReviewAssignmentCommand(id, req.TeacherId, req.Score, req.Comments);
                await handler.HandleAsync(cmd, ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // ── Phase 7: recipients + review-gate + submission (spec §8/§9) ────────

        // Publish recipients (spec §12).
        group.MapGet("/{id:guid}/recipients", async (
            Guid id,
            [FromServices] IQueryHandler<ListAssignmentRecipients, AssignmentRecipientDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListAssignmentRecipients(id), ct)));

        // Submissions for an assignment (teacher review/grade queue, spec §12).
        group.MapGet("/{id:guid}/submissions", async (
            Guid id,
            [FromServices] IQueryHandler<ListSubmissionsByAssignment, SubmissionForReviewDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListSubmissionsByAssignment(id), ct)));

        // Submission with version history + review (spec §9 GET .../submission).
        group.MapGet("/{id:guid}/students/{studentId:guid}/submission", async (
            Guid id,
            Guid studentId,
            [FromServices] IQueryHandler<GetSubmission, SubmissionDetailDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetSubmission(id, studentId), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // Guardian review (Primary). spec §9: .../students/{studentId}/guardian-review.
        group.MapPost("/{id:guid}/students/{studentId:guid}/guardian-review", async (
            Guid id,
            Guid studentId,
            [FromBody] ReviewSubmissionGateRequest req,
            [FromServices] ISubmissionRepository submissionRepo,
            [FromServices] ICommandHandler<ReviewSubmissionGateCommand> handler,
            CancellationToken ct) =>
        {
            var gate = await submissionRepo.GetGateByAssignmentStudentAsync(id, studentId, ct);
            if (gate is null) return Results.NotFound();
            try
            {
                await handler.HandleAsync(new ReviewSubmissionGateCommand(gate.Id, req.ReviewerGuardianId, req.Approve, req.Comment), ct);
                return Results.NoContent();
            }
            catch (GuardianSubmissionGateNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // Teacher/admin enables student self-submit directly (spec §9: .../enable-submission).
        group.MapPost("/{id:guid}/students/{studentId:guid}/enable-submission", async (
            Guid id,
            Guid studentId,
            [FromBody] EnableStudentSubmissionRequest req,
            [FromServices] ISubmissionRepository submissionRepo,
            [FromServices] ICommandHandler<EnableStudentSubmissionCommand> handler,
            CancellationToken ct) =>
        {
            var gate = await submissionRepo.GetGateByAssignmentStudentAsync(id, studentId, ct);
            if (gate is null) return Results.NotFound();
            try
            {
                await handler.HandleAsync(new EnableStudentSubmissionCommand(gate.Id, req.ReviewerGuardianId), ct);
                return Results.NoContent();
            }
            catch (GuardianSubmissionGateNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // Guardian submits on behalf (spec §9: POST /assignments/{id}/students/{studentId}/submit-on-behalf).
        group.MapPost("/{id:guid}/students/{studentId:guid}/submit-on-behalf", async (
            Guid id,
            Guid studentId,
            [FromBody] SubmitAssignmentOnBehalfRequest req,
            [FromServices] ICommandHandler<SubmitAssignmentOnBehalfCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SubmitAssignmentOnBehalfCommand(id, studentId, req.GuardianId, req.Content), ct);
                return Results.NoContent();
            }
            catch (GuardianSubmissionGateNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        // Student self-submit (spec §9: POST /assignments/{id}/students/{studentId}/submission).
        group.MapPost("/{id:guid}/students/{studentId:guid}/submission", async (
            Guid id,
            Guid studentId,
            [FromBody] CreateStudentSubmissionRequest req,
            [FromServices] ICommandHandler<CreateStudentSubmissionCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new CreateStudentSubmissionCommand(id, studentId, req.Content), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: 403);
            }
        });

        // Teacher grades a submission (spec §9: .../students/{studentId}/submission/review).
        group.MapPost("/{id:guid}/students/{studentId:guid}/submission/review", async (
            Guid id,
            Guid studentId,
            [FromBody] ReviewSubmissionRequest req,
            [FromServices] ISubmissionRepository submissionRepo,
            [FromServices] ICommandHandler<ReviewSubmissionCommand> handler,
            CancellationToken ct) =>
        {
            var submission = await submissionRepo.GetSubmissionByAssignmentStudentAsync(id, studentId, ct);
            if (submission is null) return Results.NotFound();
            try
            {
                await handler.HandleAsync(new ReviewSubmissionCommand(submission.Id, req.TeacherId, req.Score, req.Grade, req.Comments), ct);
                return Results.NoContent();
            }
            catch (SubmissionNotFoundException)
            {
                return Results.NotFound();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: 403);
            }
        });

        group.MapGet("/{id:guid}/submissions/review-queue", async (
            Guid id,
            Guid teacherId,
            [FromServices] IQueryHandler<GetSubmissionsForReview, SubmissionForReviewDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new GetSubmissionsForReview(teacherId), ct)));

        group.MapGet("/{id:guid}/gates/student/{studentId:guid}", async (
            Guid id,
            Guid studentId,
            [FromServices] IQueryHandler<GetGuardianGate, GuardianGateDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetGuardianGate(id, studentId), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return group;
    }
}
