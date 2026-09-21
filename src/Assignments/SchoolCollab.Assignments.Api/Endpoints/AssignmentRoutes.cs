using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ApproveAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CloseAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateStudentSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DeleteAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DuplicateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RejectAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmissionGate;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ScheduleAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.StageAttachmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentForApprovalCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentOnBehalf;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UnpublishAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionsDraft;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.QuestionsDraft;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetGuardianGate;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetNotificationFailures;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmissionsForReview;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentRecipients;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListSubmissionsByAssignment;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.EnableStudentSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.OverrideStudentSubmissionAttempts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RecordModuleProgress;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.Ward;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Api.Endpoints;

/// <summary>WS-B2 (spec §3.4 line 73) — route body for staging a questions
/// draft (PUT /assignments/{id:guid}/questions-draft).</summary>
public sealed record StageQuestionsDraftBody(IReadOnlyList<NewQuestionDto> Questions);

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

        // ── Effective guardian-signature default (WS-C1 / spec §7 Q1) ──
        // The literal segment wins over the {id:guid} template above (a
        // non-GUID segment never matches a guid route), so the always-200
        // fail-open resolution is reachable by the create-wizard pre-fill.
        group.MapGet("/signature-default", async (
            [FromQuery] Guid? gradeLevelId,
            [FromServices] SchoolCollab.Assignments.Core.Services.ISignatureDefaultResolver resolver,
            CancellationToken ct) =>
        {
            var requiresSignature = await resolver.ResolveRequiresSignatureDefaultAsync(gradeLevelId, ct);
            return Results.Ok(new { requiresSignature });
        });

        // ── Guardian sign-off consent language (WS-C1/C2 / spec §3.2 line 53) ──
        // Always 200 + the resolved consent text (tenant override or embedded
        // default via the fail-open resolver) so the sign page is never blocked.
        // Literal segment wins over the {id:guid} template (the /signature-default
        // precedent).
        group.MapGet("/signature-consent-text", async (
            [FromServices] SchoolCollab.Assignments.Core.Services.ISignatureConsentTextResolver resolver,
            CancellationToken ct) =>
        {
            var consentText = await resolver.ResolveConsentTextAsync(ct);
            return Results.Ok(new { consentText });
        });

        // ── Versioned questions draft (WS-B2 / spec §3.4 line 73) ──
        group.MapGet("/{id:guid}/questions-draft", async (
            Guid id,
            [FromServices] IQueryHandler<GetQuestionsDraftQuery, IReadOnlyList<NewQuestionDto>?> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new GetQuestionsDraftQuery(id), ct);
                return result is null ? Results.NoContent() : Results.Ok(new { questions = result });
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidQuestionsDraftException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: ex.Message);
            }
        });

        group.MapPut("/{id:guid}/questions-draft", async (
            Guid id,
            [FromBody] StageQuestionsDraftBody body,
            [FromServices] ICommandHandler<StageQuestionsDraftCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new StageQuestionsDraftCommand(id, body.Questions), ct);
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
            catch (InvalidQuestionsDraftException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: ex.Message);
            }
        });

        group.MapPost("/{id:guid}/questions-draft/confirm", async (
            Guid id,
            [FromServices] ICommandHandler<ConfirmQuestionsDraftCommand> confirmHandler,
            [FromServices] IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?> readHandler,
            CancellationToken ct) =>
        {
            try
            {
                await confirmHandler.HandleAsync(new ConfirmQuestionsDraftCommand(id), ct);
                var summary = await readHandler.HandleAsync(new GetAssignmentByIdQuery(id), ct);
                return Results.Ok(summary);
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidQuestionsDraftException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: ex.Message);
            }
        });

        group.MapDelete("/{id:guid}/questions-draft", async (
            Guid id,
            [FromServices] ICommandHandler<DiscardQuestionsDraftCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DiscardQuestionsDraftCommand(id), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidQuestionsDraftException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: ex.Message);
            }
        });

        // ── Org-level AI-prompt lock (WS-B2 / spec §3.4 line 70) ──
        // Always 200 + the resolved lock (fail-open false mirrors the resolver
        // posture) so the create wizard is never blocked. Literal segment wins
        // over the {id:guid} template (the /signature-default precedent).
        group.MapGet("/ai-prompt-policy", async (
            [FromServices] SchoolCollab.Assignments.Core.Services.IAiPromptPolicyResolver resolver,
            CancellationToken ct) =>
        {
            var aiPromptLocked = await resolver.ResolveAiPromptLockedAsync(ct);
            return Results.Ok(new { aiPromptLocked });
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
                    req.Attachments,
                    req.ContentModules,
                    req.Resources,
                    req.ArchiveGraceDays,
                    // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold +
                    // attempt cap on the wire surface.
                    req.PassScore,
                    req.MaxAttempts,
                    // WS-C1 (spec §7 Q1): guardian-signature snapshot.
                    req.RequiresSignature,
                    // WS-B2 (spec §3.4 line 70): optional per-difficulty counts.
                    req.DifficultyEasyCount,
                    req.DifficultyMediumCount,
                    req.DifficultyHardCount);
                var id = await handler.HandleAsync(cmd, ct);
                return Results.Created($"/assignments/{id}", new { id });
            }
            catch (AssignmentQuestionValidationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (AssignmentContentValidationException ex)
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
            // ar-20: real-auth rejections map distinctly (never a 500). A principal with no
            // usable teacher_id claim in real-auth mode → 403; an unknown teacher → 409.
            catch (MissingTeacherPrincipalException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
            }
            catch (UnknownTeacherException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: ex.Message);
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
                    req.Attachments,
                    req.ContentModules,
                    req.Resources,
                    req.ArchiveGraceDays,
                    // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold +
                    // attempt cap on the wire surface.
                    req.PassScore,
                    req.MaxAttempts,
                    // WS-C1 (spec §7 Q1): guardian-signature round-trip.
                    req.RequiresSignature,
                    // WS-B2 (spec §3.4 line 70): optional per-difficulty counts.
                    req.DifficultyEasyCount,
                    req.DifficultyMediumCount,
                    req.DifficultyHardCount);
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
            catch (AssignmentContentValidationException ex)
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
            // WS-A2 / spec §7 Q2: the typed approval guard surfaces as 400
            // so the UI can render the inline error.
            catch (AssignmentApprovalRequiredException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            // WS-A2 / decision (a): archived assignments are read-only;
            // mirror the create/update/delete routes' InvalidOperationException
            // -> 400 mapping so the call surfaces a domain message instead
            // of a 500.
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        // WS-A2 / spec §3.5 step 2: schedule the assignment to auto-publish
        // at the given moment. The scheduled-publish sweep dispatches the
        // existing publish command when AvailableFromUtc arrives.
        group.MapPost("/{id:guid}/schedule", async (
            Guid id,
            [FromBody] ScheduleAssignmentRequest req,
            [FromServices] ICommandHandler<ScheduleAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new ScheduleAssignmentCommand(id, req.AvailableFromUtc), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (AssignmentApprovalRequiredException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            // ArgumentException for past-date; InvalidOperationException for
            // wrong-status (matches the unpublish route's existing pattern).
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        // WS-A2 / spec §7 Q2: submit a draft for approval. No body — the
        // domain method is the entire side effect.
        group.MapPost("/{id:guid}/submit-for-approval", async (
            Guid id,
            [FromServices] ICommandHandler<SubmitAssignmentForApprovalCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SubmitAssignmentForApprovalCommand(id), ct);
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

        // WS-A2 / spec §7 Q2: approve a pending assignment. ApproverId is
        // the identity placeholder until identity wiring lands.
        group.MapPost("/{id:guid}/approve", async (
            Guid id,
            [FromBody] ApproveAssignmentRequest req,
            [FromServices] ICommandHandler<ApproveAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new ApproveAssignmentCommand(id, req.ApproverId), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            // ar-20: real-auth rejection — no usable teacher_id claim ⇒ 403 (never a 500).
            catch (MissingTeacherPrincipalException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
            }
            // ar-24: cross-tenant rejection — foreign-tenant approver ⇒ 403 (never a 500).
            catch (TeacherTenantMismatchException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
            }
        });

        // WS-A2 / spec §7 Q2: reject a pending assignment.
        group.MapPost("/{id:guid}/reject", async (
            Guid id,
            [FromBody] RejectAssignmentRequest req,
            [FromServices] ICommandHandler<RejectAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RejectAssignmentCommand(id, req.ApproverId), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            // ar-20: real-auth rejection — no usable teacher_id claim ⇒ 403 (never a 500).
            catch (MissingTeacherPrincipalException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
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
            // WS-A2 / decision (a): archived assignments are read-only;
            // mirror the publish route's InvalidOperationException -> 400
            // mapping so the call surfaces a domain message instead of
            // a 500.
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        group.MapPost("/{id:guid}/duplicate", async (
            Guid id,
            [FromServices] ICommandHandler<DuplicateAssignmentCommand, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var newId = await handler.HandleAsync(new DuplicateAssignmentCommand(id), ct);
                return Results.Created($"/assignments/{newId}", new { id = newId });
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
            // ar-20: real-auth rejection — no usable teacher_id claim ⇒ 403 (never a 500).
            catch (MissingTeacherPrincipalException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
            }
            // ar-24: cross-tenant rejection — foreign-tenant teacher ⇒ 403 (never a 500).
            catch (TeacherTenantMismatchException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
            }
        });

        // ── Phase 7: recipients + review-gate + submission (spec §8/§9) ────────

        // Publish recipients (spec §12).
        group.MapGet("/{id:guid}/recipients", async (
            Guid id,
            [FromServices] IQueryHandler<ListAssignmentRecipients, AssignmentRecipientDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListAssignmentRecipients(id), ct)));

        // WS-E2 (ar-16): failed notification deliveries for an assignment — the
        // read side the ar-17 Admin failure surface renders. Failed rows only and
        // tenant-scoped by the context's global query filter.
        group.MapGet("/{id:guid}/notification-failures", async (
            Guid id,
            [FromServices] IQueryHandler<GetNotificationFailures, NotificationFailureDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new GetNotificationFailures(id), ct)));

        // WS-E1 (ar-14-deep-links): idempotent first-visit deep-link stamp, invoked
        // server-side by the Families landing. Marks the recipient OpenedAt at most
        // once (first visit feeding the Sent → Viewed chain); subsequent landings are
        // no-ops. 404 when the (assignment, contact) recipient row is unknown.
        group.MapPost("/{id:guid}/recipients/{contactId:guid}/opened", async (
            Guid id,
            Guid contactId,
            [FromServices] ISubmissionRepository submissionRepository,
            CancellationToken ct) =>
        {
            var recipient = await submissionRepository.GetRecipientAsync(id, contactId, ct);
            if (recipient is null) return Results.NotFound();
            if (recipient.OpenedAt is null)
            {
                recipient.MarkOpened();
                submissionRepository.Update(recipient);
                await submissionRepository.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        });

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
                await handler.HandleAsync(new SubmitAssignmentOnBehalfCommand(id, studentId, req.GuardianId, req.Content, req.Answers), ct);
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
            // WS-A3 (spec §3.3 + §7 Q4): on-behalf parity with the
            // student path — 409 cap, 400 answers, 404 assignment.
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (SubmissionAttemptsExhaustedException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            catch (SubmissionAnswerValidationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        // Student self-submit (spec §9: POST /assignments/{id}/students/{studentId}/submission).
        group.MapPost("/{id:guid}/students/{studentId:guid}/submission", async (
            Guid id,
            Guid studentId,
            [FromBody] CreateStudentSubmissionRequest req,
            [FromServices] ICommandHandler<CreateStudentSubmissionCommand, SubmissionFeedbackDto?> handler,
            CancellationToken ct) =>
        {
            try
            {
                var feedback = await handler.HandleAsync(new CreateStudentSubmissionCommand(id, studentId, req.Content, req.Answers), ct);
                // WS-A3 (spec §3.3): InstantGraded → 200 with feedback envelope;
                // AutoGraded / TeacherGraded → 204 NoContent.
                return feedback is null ? Results.NoContent() : Results.Ok(feedback);
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            // WS-A3 / spec §7 Q4: cap exhausted → 409.
            catch (SubmissionAttemptsExhaustedException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            // WS-D1 / spec §3.3: required content modules not completed → 409
            // (the module-progress gate on submission).
            catch (RequiredModuleIncompleteException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            // WS-A3 / spec §3.3: answer validation → 400 (matches the
            // group's catch pattern).
            catch (SubmissionAnswerValidationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: 403);
            }
        });

        // WS-A3 (spec §7 Q4) — teacher override on the MaxAttempts cap
        // for one submission. Route resolves (assignmentId, studentId) →
        // submission and 404s before dispatching (the enable-submission
        // route resolution precedent). The handler dispatches the
        // submission-id-precise command (decision (f) recorded
        // adjustment).
        group.MapPost("/{id:guid}/students/{studentId:guid}/override-attempts", async (
            Guid id,
            Guid studentId,
            [FromBody] OverrideStudentSubmissionAttemptsRequest req,
            [FromServices] ISubmissionRepository submissionRepo,
            [FromServices] ICommandHandler<OverrideStudentSubmissionAttemptsCommand> handler,
            CancellationToken ct) =>
        {
            var submission = await submissionRepo.GetSubmissionByAssignmentStudentAsync(id, studentId, ct);
            if (submission is null) return Results.NotFound();
            try
            {
                await handler.HandleAsync(new OverrideStudentSubmissionAttemptsCommand(submission.Id, req.TeacherId), ct);
                return Results.NoContent();
            }
            catch (SubmissionNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            // ar-20: real-auth rejection — no usable teacher_id claim ⇒ 403 (never a 500).
            catch (MissingTeacherPrincipalException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
            }
            // ar-24: cross-tenant rejection — foreign-tenant teacher ⇒ 403 (never a 500).
            catch (TeacherTenantMismatchException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
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
            // ar-20: real-auth rejection — no usable teacher_id claim ⇒ 403 (never a 500).
            catch (MissingTeacherPrincipalException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
            }
            // ar-24: cross-tenant rejection — foreign-tenant teacher ⇒ 403 (never a 500).
            catch (TeacherTenantMismatchException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
            }
        });

        group.MapGet("/{id:guid}/submissions/review-queue", async (
            Guid id,
            Guid teacherId,
            [FromServices] ICurrentUser currentUser,
            [FromServices] IFeatureFlagService featureFlags,
            [FromServices] IQueryHandler<GetSubmissionsForReview, SubmissionForReviewDto[]> handler,
            CancellationToken ct) =>
        {
            try
            {
                // ar-24: the acting teacher is resolved principal-first (R4).
                var isRealAuth = !featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth);
                var effectiveTeacherId = currentUser.TeacherId
                    ?? (isRealAuth
                        ? throw new MissingTeacherPrincipalException("GetSubmissionsForReview")
                        : teacherId);
                return Results.Ok(await handler.HandleAsync(new GetSubmissionsForReview(effectiveTeacherId), ct));
            }
            catch (MissingTeacherPrincipalException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
            }
            catch (TeacherTenantMismatchException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
            }
        });

        // ── WS-C1/C2: guardian sign-off (spec §3.2 / §5 / §6) ───────────────────

        // Per-ward sign-off status rows for the teacher surface (the card on
        // Detail). 404 when the assignment is missing.
        group.MapGet("/{id:guid}/sign-off-statuses", async (
            Guid id,
            [FromServices] IQueryHandler<ListSignOffStatusesQuery, IReadOnlyList<SignOffStatusDto>> handler,
            CancellationToken ct) =>
        {
            try
            {
                var statuses = await handler.HandleAsync(new ListSignOffStatusesQuery(id), ct);
                return Results.Ok(statuses);
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // Guardian e-signs a ward's submission. Captures the caller IP + user-agent
        // at the endpoint and threads them into the command for the audit event
        // (spec §3.2 line 53 / §6 auditability line 116). Returns the refreshed
        // per-ward status row (200).
        group.MapPost("/{id:guid}/students/{studentId:guid}/sign-off", async (
            Guid id,
            Guid studentId,
            [FromBody] SignOffSubmissionRequest req,
            HttpContext http,
            [FromServices] ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto> handler,
            CancellationToken ct) =>
        {
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = http.Request.Headers.UserAgent.ToString();
            try
            {
                var result = await handler.HandleAsync(new SignOffSubmissionCommand(
                    id, studentId, req.GuardianId,
                    (SignatureType)(int)req.SignatureType,
                    req.TypedSignature,
                    ip, userAgent), ct);
                return Results.Ok(result);
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (SubmissionNotFoundException)
            {
                return Results.NotFound();
            }
            catch (SubmissionAlreadySignedException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            catch (SubmissionSignOffStateException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            catch (SubmissionLockedException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            catch (GuardianNotAuthorizedException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        // Teacher reassigns the expected signer to another linked guardian.
        group.MapPost("/{id:guid}/students/{studentId:guid}/sign-off/reassign", async (
            Guid id,
            Guid studentId,
            [FromBody] ReassignSignOffRequest req,
            [FromServices] ICommandHandler<ReassignSignOffCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new ReassignSignOffCommand(id, studentId, req.NewGuardianId), ct);
                return Results.NoContent();
            }
            catch (SubmissionNotFoundException)
            {
                return Results.NotFound();
            }
            catch (GuardianNotAuthorizedException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            catch (SubmissionSignOffStateException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        // Teacher finalizes a signed sign-off (C4 locking; teacher action only).
        group.MapPost("/{id:guid}/students/{studentId:guid}/sign-off/finalize", async (
            Guid id,
            Guid studentId,
            [FromServices] ICommandHandler<FinalizeSignOffCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new FinalizeSignOffCommand(id, studentId), ct);
                return Results.NoContent();
            }
            catch (SubmissionNotFoundException)
            {
                return Results.NotFound();
            }
            catch (SubmissionSignOffStateException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            catch (AssignmentCertificateException ex)
            {
                // C3: a certificate generation/store failure fails the finalize
                // (transactional) and maps to 502 (the ar-2 provider-failure
                // precedent) so the client can distinguish it from the 500 default.
                return Results.Problem(ex.Message, statusCode: 502);
            }
        });

        // C3 — the finalized certificate PDF (GET /{id}/students/{studentId}/certificate).
        // 404 when the pair has no signature event or no certificate was generated;
        // the stored file is streamed as application/pdf with a download name.
        group.MapGet("/{id:guid}/students/{studentId:guid}/certificate", async (
            Guid id,
            Guid studentId,
            [FromServices] ISubmissionRepository submissionRepository,
            [FromServices] IFileStore fileStore,
            CancellationToken ct) =>
        {
            var signatureEvent = await submissionRepository.GetSignatureEventByAssignmentStudentAsync(
                id, studentId, ct);
            if (signatureEvent is null || string.IsNullOrWhiteSpace(signatureEvent.CertificateStoragePath))
            {
                return Results.NotFound();
            }

            Stream? stream;
            try
            {
                stream = await fileStore.OpenReadAsync(signatureEvent.CertificateStoragePath, ct);
            }
            catch (FileNotFoundException)
            {
                // Stored reference present but the backing file is gone.
                return Results.NotFound();
            }

            return Results.Stream(stream, "application/pdf",
                fileDownloadName: $"certificate-{id:N}-{studentId:N}.pdf");
        });

        // The single aggregate the guardian sign page consumes (one call).
        group.MapGet("/{id:guid}/students/{studentId:guid}/sign-off", async (
            Guid id,
            Guid studentId,
            [FromServices] IQueryHandler<GetSignOffContextQuery, SignOffContextDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new GetSignOffContextQuery(id, studentId), ct);
                return Results.Ok(result);
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (SubmissionNotFoundException)
            {
                return Results.NotFound();
            }
            catch (SubmissionSignOffStateException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
        });

        group.MapGet("/{id:guid}/gates/student/{studentId:guid}", async (
            Guid id,
            Guid studentId,
            [FromServices] IQueryHandler<GetGuardianGate, GuardianGateDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetGuardianGate(id, studentId), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // ── WS-A1 / FR-210-212: stage one file for the create payload
        // (EC-4 reconciliation: stage-at-selection, decision (b)).
        // Antiforgery is disabled: the Api is consumed server-to-server by
        // the admin Blazor host with token-based auth, not browser forms.
        group.MapPost("/attachments/stage", async (
            [FromForm] IFormFile? file,
            [FromServices] ICommandHandler<StageAttachmentCommand, StagedAttachmentDto> handler,
            CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "A file is required." });
            }
            await using var stream = file.OpenReadStream();
            try
            {
                var result = await handler.HandleAsync(
                    new StageAttachmentCommand(stream, file.FileName, file.ContentType, file.Length),
                    ct);
                return Results.Ok(result);
            }
            catch (StagedFileRejectedException ex) when (ex.IsSizeLimit)
            {
                return Results.Json(new { ex.Message },
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }
            catch (StagedFileRejectedException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        }).DisableAntiforgery();

        // ── WS-D1 (spec §3.3): per-ward module-progress heartbeats ─────
        // 204 on a recorded/upserted report; 404 when the assignment or the
        // module is missing. Same auth posture as the other student-scoped
        // routes (token-auth re-scoping is F1 / slice 2b — recorded OUT).
        group.MapPost("/{id:guid}/students/{studentId:guid}/modules/{moduleId:guid}/progress", async (
            Guid id,
            Guid studentId,
            Guid moduleId,
            [FromBody] RecordModuleProgressRequest req,
            [FromServices] ICommandHandler<RecordModuleProgressCommand, bool> handler,
            CancellationToken ct) =>
        {
            try
            {
                var ok = await handler.HandleAsync(new RecordModuleProgressCommand(id, studentId, moduleId, req.Percent), ct);
                return ok ? Results.NoContent() : Results.NotFound();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // WS-D1/WS-A5: the ward-facing assignment view — modules with per-ward
        // progress + the questions-unlocked gate flag (the 2b player binds to this).
        group.MapGet("/{id:guid}/students/{studentId:guid}/modules", async (
            Guid id,
            Guid studentId,
            [FromServices] IQueryHandler<GetWardAssignmentView, WardAssignmentViewDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetWardAssignmentView(id, studentId), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return group;
    }

    /// <summary>WS-A5 — the ward assignment list, mounted under a sibling
    /// <c>/students</c> group (not under <c>/assignments</c>) so the resource
    /// path is <c>GET /students/{studentId}/assignments</c> exactly.</summary>
    public static RouteGroupBuilder MapWardAssignmentRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/{studentId:guid}/assignments", async (
            Guid studentId,
            [FromServices] IQueryHandler<ListWardAssignments, WardAssignmentListItemDto[]> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ListWardAssignments(studentId), ct);
            return Results.Ok(result);
        });

        return group;
    }
}
