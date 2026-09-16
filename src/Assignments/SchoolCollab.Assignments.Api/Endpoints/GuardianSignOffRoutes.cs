using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Api.Auth;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Api.Endpoints;

/// <summary>
/// WS-F3 (ar-15-signoff-relocation, decisions (a)/(c)/(d)) — the public, token-gated
/// guardian sign-off route group. Mapped OUTSIDE the teacher/admin-gated
/// <c>/assignments</c> group with NO <see cref="Microsoft.AspNetCore.Authorization"/>:
/// the credential is the <c>x-deeplink-token</c> header, validated by
/// <see cref="GuardianTokenEndpointFilter"/> (the ar-14 public-group precedent). The
/// group reuses the SAME query/command handlers as the teacher routes — no dual-auth,
/// no teacher-route churn. The acting <see cref="Guid"/> guardian is threaded from
/// <see cref="HttpContext.Items"/> (resolved server-side from the token); the request
/// body never carries a GuardianId (decision (b)). Reassign/finalize are NOT exposed
/// here (teacher-only, decision (c)).
/// </summary>
public static class GuardianSignOffRoutes
{
    /// <summary>Maps the public <c>/guardian</c> sign-off route group.</summary>
    public static WebApplication MapGuardianSignOffRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/guardian");

        // The single aggregate the guardian sign page consumes (one call).
        group.MapGet("/assignments/{id:guid}/students/{studentId:guid}/sign-off", async (
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
        }).AddEndpointFilter<GuardianTokenEndpointFilter>();

        // Guardian e-signs the ward's submission. The acting GuardianId is taken from
        // HttpContext.Items (token-resolved), NEVER from the body (decision (b)).
        group.MapPost("/assignments/{id:guid}/students/{studentId:guid}/sign-off", async (
            Guid id,
            Guid studentId,
            [FromBody] GuardianSignOffSubmissionRequest req,
            HttpContext http,
            [FromServices] ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto> handler,
            CancellationToken ct) =>
        {
            if (http.Items[GuardianTokenEndpointFilter.GuardianIdItemKey] is not Guid guardianId)
            {
                return Results.Unauthorized();
            }
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = http.Request.Headers.UserAgent.ToString();

            try
            {
                var result = await handler.HandleAsync(new SignOffSubmissionCommand(
                    id, studentId, guardianId,
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
        }).AddEndpointFilter<GuardianTokenEndpointFilter>();

        // C3 — the finalized certificate PDF (guardian download path; same logic as the
        // teacher certificate route).
        group.MapGet("/assignments/{id:guid}/students/{studentId:guid}/certificate", async (
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
                return Results.NotFound();
            }

            return Results.Stream(stream, "application/pdf",
                fileDownloadName: $"certificate-{id:N}-{studentId:N}.pdf");
        }).AddEndpointFilter<GuardianTokenEndpointFilter>();

        return app;
    }
}
