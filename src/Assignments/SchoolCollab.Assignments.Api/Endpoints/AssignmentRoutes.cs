using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using SchoolCollab.Assignments.Api.Auth;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ApproveAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ArchiveAssignmentCommand;
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
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionGeneration;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RegenerateAttachmentExtraction;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.QuestionsDraft;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentAuthoringChildren;
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
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Api.Endpoints;

/// <summary>WS-B2 (spec §3.4 line 73) — route body for staging a questions
/// draft (PUT /assignments/{id:guid}/questions-draft).</summary>
public sealed record StageQuestionsDraftBody(IReadOnlyList<NewQuestionDto> Questions);

public static class AssignmentRoutes
{
    /// <summary>
    /// The seven teacher-portal GET reads (round <c>teacher-scope-auth</c> D3/D4 and round
    /// <c>assignment-rules-policy-rework</c> OD4) — exactly the routes the reader policy decorates
    /// (see <see cref="AssignmentEndpoints"/>). Their caller's <see cref="TeacherScope"/> is resolved
    /// HERE, at the endpoint, and threaded onto the query; no Core handler fetches it. Everything else
    /// the <c>/assignments</c> group serves stays in <see cref="MapAssignmentRoutes"/> and stays
    /// reachable by a role-less principal — except the one submission-grade POST, which
    /// <see cref="MapAssignmentGradeRoutes"/> mounts under its own writer sub-group
    /// (round <c>portal-submission-grade</c> D1).
    /// </summary>
    public static RouteGroupBuilder MapAssignmentReaderRoutes(this RouteGroupBuilder group)
    {
        // GET /assignments — [P1-3] the scope filter is applied by the handler AFTER the
        // tenant-wide cache read (assignments:list:{tenantId}:{status}), never inside the cached
        // delegate: filtering there would serve one teacher's list to every other caller in the
        // tenant.
        group.MapGet("/", async (
            [FromQuery] AssignmentStatus? status,
            ClaimsPrincipal user,
            [FromServices] ICurrentUser currentUser,
            [FromServices] ITeacherScopeProvider scopeProvider,
            [FromServices] IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]> handler,
            CancellationToken ct) =>
        {
            var scope = await AssignmentScopeResolver.ResolveAsync(user, currentUser, scopeProvider, ct);
            var results = await handler.HandleAsync(new ListAssignmentsQuery(status, scope), ct);
            return Results.Ok(results);
        });

        // GET /assignments/{id} — [P2-2] the read is reachable by id, so it applies the same
        // visibility rule as the list and answers 404 for an out-of-scope id.
        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            [FromServices] ICurrentUser currentUser,
            [FromServices] ITeacherScopeProvider scopeProvider,
            [FromServices] IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?> handler,
            CancellationToken ct) =>
        {
            var scope = await AssignmentScopeResolver.ResolveAsync(user, currentUser, scopeProvider, ct);
            var result = await handler.HandleAsync(new GetAssignmentByIdQuery(id, scope), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // Submissions for an assignment (teacher review/grade queue, spec §12).
        // [P2-2] the id-addressed read is gated on the assignment's visibility first — an
        // out-of-scope id is a 404, exactly like an unknown one.
        group.MapGet("/{id:guid}/submissions", async (
            Guid id,
            ClaimsPrincipal user,
            [FromServices] ICurrentUser currentUser,
            [FromServices] ITeacherScopeProvider scopeProvider,
            [FromServices] IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?> assignmentHandler,
            [FromServices] IQueryHandler<ListSubmissionsByAssignment, SubmissionForReviewDto[]> handler,
            CancellationToken ct) =>
        {
            var scope = await AssignmentScopeResolver.ResolveAsync(user, currentUser, scopeProvider, ct);
            if (!await IsAssignmentVisibleAsync(id, scope, assignmentHandler, ct))
            {
                return Results.NotFound();
            }

            return Results.Ok(await handler.HandleAsync(new ListSubmissionsByAssignment(id), ct));
        });

        // Submission with version history + review (spec §9 GET .../submission).
        // [P2-2] same visibility gate as the per-assignment submissions read above.
        group.MapGet("/{id:guid}/students/{studentId:guid}/submission", async (
            Guid id,
            Guid studentId,
            ClaimsPrincipal user,
            [FromServices] ICurrentUser currentUser,
            [FromServices] ITeacherScopeProvider scopeProvider,
            [FromServices] IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?> assignmentHandler,
            [FromServices] IQueryHandler<GetSubmission, SubmissionDetailDto?> handler,
            CancellationToken ct) =>
        {
            var scope = await AssignmentScopeResolver.ResolveAsync(user, currentUser, scopeProvider, ct);
            if (!await IsAssignmentVisibleAsync(id, scope, assignmentHandler, ct))
            {
                return Results.NotFound();
            }

            var result = await handler.HandleAsync(new GetSubmission(id, studentId), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/{id:guid}/submissions/review-queue", async (
            Guid id,
            Guid teacherId,
            ClaimsPrincipal user,
            [FromServices] ICurrentUser currentUser,
            [FromServices] IFeatureFlagService featureFlags,
            [FromServices] ITeacherScopeProvider scopeProvider,
            [FromServices] IQueryHandler<GetSubmissionsForReview, SubmissionForReviewDto[]> handler,
            CancellationToken ct) =>
        {
            try
            {
                var scope = await AssignmentScopeResolver.ResolveAsync(user, currentUser, scopeProvider, ct);

                // ar-24: the acting teacher is resolved principal-first (R4).
                var isRealAuth = !featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth);
                var effectiveTeacherId = currentUser.TeacherId
                    ?? (isRealAuth
                        ? throw new MissingTeacherPrincipalException("GetSubmissionsForReview")
                        : teacherId);

                // [P1-4] the scope — not the wire value — decides what the queue returns: a
                // teacher-only principal with no teacher_id claim carries an EMPTY scope, so the
                // dev `teacherId` fallback above cannot widen the result set.
                return Results.Ok(await handler.HandleAsync(new GetSubmissionsForReview(effectiveTeacherId, scope), ct));
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

        // Per-ward sign-off status rows for the teacher surface (the card on
        // Detail). [P2-2] this read is id-addressed too, so it goes through the same
        // visibility gate as `GET /{id}/submissions` — an out-of-scope id answers like an
        // unknown one (404) instead of returning the rows behind it. 404 when the
        // assignment is missing.
        group.MapGet("/{id:guid}/sign-off-statuses", async (
            Guid id,
            ClaimsPrincipal user,
            [FromServices] ICurrentUser currentUser,
            [FromServices] ITeacherScopeProvider scopeProvider,
            [FromServices] IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?> assignmentHandler,
            [FromServices] IQueryHandler<ListSignOffStatusesQuery, IReadOnlyList<SignOffStatusDto>> handler,
            CancellationToken ct) =>
        {
            var scope = await AssignmentScopeResolver.ResolveAsync(user, currentUser, scopeProvider, ct);
            if (!await IsAssignmentVisibleAsync(id, scope, assignmentHandler, ct))
            {
                return Results.NotFound();
            }

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

        // ── Effective assignment policy (WS-C1 / spec §7 Q1; round assignment-rules-policy-rework D2/OD4) ──
        // The whole resolved policy for the authoring page's Rules readouts — approval, signature,
        // guardian review and the archive window — with the D4 implication already applied by the
        // resolver. Always 200: the resolution is fail-open (an unreachable policy source degrades
        // to "nothing configured"), so the page's readouts can never be blocked. Null
        // gradeLevelId resolves the tenant-global default; a grade id resolves the grade override
        // falling back to the tenant default. The literal segment wins over the {id:guid} template
        // (a non-GUID segment never matches a guid route).
        group.MapGet("/effective-policy", async (
            [FromQuery] Guid? gradeLevelId,
            [FromServices] SchoolCollab.Assignments.Core.Services.IAssignmentPolicyResolver resolver,
            CancellationToken ct) =>
        {
            var policy = await resolver.ResolveAsync(gradeLevelId, ct);
            return Results.Ok(policy);
        });

        return group;
    }

    /// <summary>
    /// The one teacher-portal WRITE route (round <c>portal-submission-grade</c> D1) — the
    /// submission-grade POST the writer policy decorates (see <see cref="AssignmentEndpoints"/>).
    /// Its body moved <b>verbatim</b> out of <see cref="MapAssignmentRoutes"/> when the route gained
    /// the writer policy: same (assignment, student) → submission resolution, same 404 before
    /// dispatching the command, same four catch arms. Unlike the reader's six GETs, this route is a
    /// write, so it is the group's only route that is neither reader-decorated nor reachable by a
    /// role-less principal.
    /// </summary>
    public static RouteGroupBuilder MapAssignmentGradeRoutes(this RouteGroupBuilder group)
    {
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

        return group;
    }

    /// <summary>
    /// [P2-2] Whether the id-addressed assignment is inside the caller's scope. The scope-aware
    /// detail read is the single gate, so a scoped caller's out-of-scope id answers like an
    /// unknown one (404) instead of leaking the rows behind it.
    /// </summary>
    private static async Task<bool> IsAssignmentVisibleAsync(
        Guid id,
        TeacherScope scope,
        IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?> assignmentHandler,
        CancellationToken ct)
        => await assignmentHandler.HandleAsync(new GetAssignmentByIdQuery(id, scope), ct) is not null;

    public static RouteGroupBuilder MapAssignmentRoutes(this RouteGroupBuilder group)
    {
        // ── Authoring child read (assignment-authoring P1 rework) ─────────────
        // The Edit surface loads one assignment's persisted questions / attachments /
        // resources BEFORE it renders those editors: a non-null-but-empty collection
        // makes PUT /assignments/{id} full-replace (and therefore delete) the
        // persisted children. Same shape as the /{id:guid} read above — 404 when the
        // assignment is absent (also for a cross-tenant id, via the tenant filter),
        // 200 with the body otherwise. No authorization beyond the group's.
        group.MapGet("/{id:guid}/authoring", async (
            Guid id,
            [FromServices] IQueryHandler<GetAssignmentAuthoringChildrenQuery, AssignmentAuthoringChildrenDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetAssignmentAuthoringChildrenQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // ── Guardian sign-off consent language (WS-C1/C2 / spec §3.2 line 53) ──
        // Always 200 + the resolved consent text (tenant override or embedded
        // default via the fail-open resolver) so the sign page is never blocked.
        // Literal segment wins over the {id:guid} template (the /effective-policy
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
        // over the {id:guid} template (the /effective-policy precedent).
        group.MapGet("/ai-prompt-policy", async (
            [FromServices] SchoolCollab.Assignments.Core.Services.IAiPromptPolicyResolver resolver,
            CancellationToken ct) =>
        {
            var aiPromptLocked = await resolver.ResolveAiPromptLockedAsync(ct);
            return Results.Ok(new { aiPromptLocked });
        });

        // ── Audience & Targets live recipient preview (TGT-16 / D-4) ──
        // Advisory, read-only, never blocks save: the same resolver + contact + policy chain the
        // publish handler runs (TGT-9), applied to the CURRENT constraint set so "contacts
        // reachable" means exactly what publish would send. Literal segment wins over the
        // {id:guid} template (the /effective-policy precedent).
        //
        // Failure posture: any resolve/transport failure returns 200 with all-zero counts and
        // PreviewDegraded = true, which the compartment renders as an inline "Preview
        // unavailable" note — publish re-resolves fresh server-side.
        //
        // `gradeLevelIds` (plan-review P2-n2, reworked by round drop-primary-grade): the route has
        // no assignment, so publish's two derived legs — the ResolveSubscribersRequest grade cohort
        // that carries each targeted grade's teachers, and the policy-scope grade — are reproduced
        // from the POSTED grade-target set, exactly as publish derives them from the persisted one.
        // The old authored `primaryGradeId` query param is gone with the primary grade itself.
        //
        // A missing current period is a resolve-level condition the Students `by-target` leg
        // answers with "no matches" rather than an error, so it surfaces as zero counts, not as
        // PreviewDegraded.
        group.MapGet("/recipient-preview", async (
            [FromQuery] bool allStudents,
            [FromQuery] Guid[] gradeLevelIds,
            [FromQuery] Guid[] streamCodedValueIds,
            [FromQuery] Guid[] studentIds,
            [FromQuery] Guid[] activityGroupIds,
            [FromServices] IAssignmentTargetResolver targetResolver,
            [FromServices] IContactResolver contactResolver,
            [FromServices] INotificationPolicyResolver policyResolver,
            [FromServices] IAssignmentPolicyResolver assignmentPolicyResolver,
            [FromServices] ITenantProvider tenantProvider,
            CancellationToken ct) =>
        {
            // Advisory zero for any failure — the whole preview is best-effort (D-4).
            var degraded = new RecipientPreviewDto(0, 0, 0, PreviewDegraded: true);
            var tenantId = tenantProvider.GetTenantContext().TenantId;

            try
            {
                var constraints = new List<TargetConstraint>();
                constraints.AddRange((gradeLevelIds ?? []).Select(id => new TargetConstraint(TargetKind.GradeLevel, id)));
                constraints.AddRange((streamCodedValueIds ?? []).Select(id => new TargetConstraint(TargetKind.Stream, id)));
                constraints.AddRange((studentIds ?? []).Select(id => new TargetConstraint(TargetKind.Student, id)));
                constraints.AddRange((activityGroupIds ?? []).Select(id => new TargetConstraint(TargetKind.ActivityGroup, id)));

                var matchedStudentIds = await targetResolver.ResolveStudentIdsAsync(constraints, allStudents, ct);

                if (matchedStudentIds.Length == 0)
                {
                    return Results.Ok(new RecipientPreviewDto(0, 0, 0, PreviewDegraded: false));
                }

                // Mirror publish's own chain: resolve subscribed contacts for the resolved cohort
                // (plus each targeted grade's teacher cohort), then apply the two effective
                // policies — both derived from the posted grade-target set by the ONE rule
                // (<see cref="AssignmentPolicyScope.DeriveGrade"/>). The recipient rows are transient
                // projections — they exist only to feed the pure filter, so they carry no
                // assignment id.
                var derivedPolicyGradeId = AssignmentPolicyScope.DeriveGrade(
                    (gradeLevelIds ?? []).Distinct().ToList());

                var subscribers = await contactResolver.ResolveSubscribersAsync(
                    new ResolveSubscribersRequest(
                        tenantId, SubscriptionScope.AllAssignments, gradeLevelIds, matchedStudentIds),
                    ct);

                var projected = subscribers
                    .Select(s => AssignmentRecipient.Create(
                        tenantId,
                        Guid.Empty,
                        s.OwnerType,
                        s.OwnerId,
                        s.StudentId,
                        s.ContactId,
                        s.Channel,
                        s.Role,
                        notifyOnBroadcast: true,
                        subscriptionActive: true))
                    .ToList();

                var effectivePolicy = await policyResolver.ResolveEffectiveAsync(tenantId, derivedPolicyGradeId, ct);
                var assignmentPolicy = await assignmentPolicyResolver.ResolveAsync(derivedPolicyGradeId, ct);
                var reachable = NotificationRecipientFilter.Apply(projected, effectivePolicy, assignmentPolicy);

                var primaryContacts = reachable.Count(r => r.Role == GuardianRole.Primary);
                return Results.Ok(new RecipientPreviewDto(
                    matchedStudentIds.Length,
                    primaryContacts,
                    reachable.Count - primaryContacts,
                    PreviewDegraded: false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Advisory only (D-4): a resolve/transport failure degrades the counts rather
                // than failing the read, and publish re-resolves fresh server-side.
                return Results.Ok(degraded);
            }
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
                    req.TopicId,
                    req.DueDate, req.MaxScore,
                    req.MandatoryReview,
                    req.AiPromptOverride,
                    req.Questions,
                    req.Attachments,
                    req.ContentModules,
                    req.Resources,
                    // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold +
                    // attempt cap on the wire surface.
                    req.PassScore,
                    req.MaxAttempts,
                    // WS-B2 (spec §3.4 line 70): optional per-difficulty counts.
                    req.DifficultyEasyCount,
                    req.DifficultyMediumCount,
                    req.DifficultyHardCount,
                    // INS-1 (assignment-authoring-compartments §9): student-facing text.
                    req.Instructions,
                    // R2 (TGT-1 / D-1): the authored targeting constraints (null = none supplied).
                    req.Targets,
                    // R4 (CP-5/D23): the picked strands & lessons — opaque Students-context ids.
                    req.ContextStrandIds,
                    req.ContextLessonIds);
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
            // R2: the target validation (TGT-13 at-least-one, TGT-2 AllStudents exclusivity, the
            // D-8.1 archived-group rule) surfaces as ArgumentException — 400, never a 500. The
            // pre-R2 domain argument guards (pass score / attempts / difficulty) land on the same
            // mapping.
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
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
                    req.TopicId,
                    req.DueDate, req.MaxScore, req.MandatoryReview,
                    req.AiPromptOverride,
                    req.Questions,
                    req.Attachments,
                    req.ContentModules,
                    req.Resources,
                    // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold +
                    // attempt cap on the wire surface.
                    req.PassScore,
                    req.MaxAttempts,
                    // WS-B2 (spec §3.4 line 70): optional per-difficulty counts.
                    req.DifficultyEasyCount,
                    req.DifficultyMediumCount,
                    req.DifficultyHardCount,
                    // INS-1 (assignment-authoring-compartments §9): student-facing text.
                    req.Instructions,
                    // R2 (TGT-1 / D-1 / UX-21): full-replacement when non-null, preserve when null.
                    req.Targets,
                    // R4 (CP-10/D23): null = preserve the persisted picks, empty = clear.
                    req.ContextStrandIds,
                    req.ContextLessonIds);
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
            // R2: the target validation (TGT-13 at-least-one, TGT-2 AllStudents exclusivity, the
            // D-2 primary-grade rule, the D-8.1 archived-group rule) surfaces as
            // ArgumentException — 400, never a 500.
            catch (ArgumentException ex)
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

        // R1 (assignment-authoring-compartments §11): the Closed status's primary action is
        // Archive. Mirrors the /close route — the same command the archive sweep dispatches.
        group.MapPost("/{id:guid}/archive", async (
            Guid id,
            [FromServices] ICommandHandler<ArchiveAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new ArchiveAssignmentCommand(id), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            // Archived rows are read-only and Archive() is only valid from
            // Published/Closed — surface the domain message as a 400 rather
            // than a 500 (the /close precedent).
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

        // ── WS-C1/C2: guardian sign-off (spec §3.2 / §5 / §6) ───────────────────

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

        // ── R3 (D4/P1-2): record one AI question generation ───────────
        // The header is written at draft-stage / generate time, by the host that owns the
        // assignment, so the AI host stays stateless. The provider/model are RELAYED from the AI
        // host's own resolution (P1-1) — this host never resolves one and the author never supplies
        // one. The returned id is stamped onto every produced question so provenance survives the
        // next full-replacement save.
        group.MapPost("/{id:guid}/question-generations", async (
            Guid id,
            [FromBody] RecordQuestionGenerationRequest body,
            [FromServices] ICommandHandler<RecordQuestionGenerationCommand, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var generationId = await handler.HandleAsync(
                    new RecordQuestionGenerationCommand(
                        id,
                        body.QuestionCount,
                        body.Types,
                        body.DifficultyEasyCount,
                        body.DifficultyMediumCount,
                        body.DifficultyHardCount,
                        body.Provider,
                        body.Model),
                    ct);
                return Results.Ok(new RecordQuestionGenerationResponse(generationId));
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (AssignmentContentValidationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        // ── R3 (D3, criterion 5): regenerate one attachment's extraction ──
        // The ONLY path that rewrites a stored extraction (D3). Re-reads the blob by StoragePath,
        // replaces the outcome in place — no new attachment row, no duplicate — and fail-opens to a
        // Failed status (200 with the new status) when the blob is gone or unreadable.
        group.MapPost("/{id:guid}/attachments/{attachmentId:guid}/extract", async (
            Guid id,
            Guid attachmentId,
            [FromServices] ICommandHandler<RegenerateAttachmentExtractionCommand, AssignmentAttachmentReadDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var attachment = await handler.HandleAsync(
                    new RegenerateAttachmentExtractionCommand(id, attachmentId), ct);
                return Results.Ok(attachment);
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (AttachmentNotFoundException)
            {
                return Results.NotFound();
            }
        });

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
