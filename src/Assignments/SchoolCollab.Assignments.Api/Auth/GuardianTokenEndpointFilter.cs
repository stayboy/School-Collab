using Microsoft.AspNetCore.Http;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Api.Auth;

/// <summary>
/// WS-F3 (ar-15-signoff-relocation, decision (a)) — the token-validated credential
/// for the public guardian sign-off route group. Reads the re-minted
/// <c>x-deeplink-token</c> header the Families host attaches on its guardian calls,
/// unprotects it with the shared <see cref="DeepLinkProtector"/> + purpose, and
/// cross-checks the authoritative recipient row server-side. The acting guardian is
/// authorized for the ROUTE student via the guardian link (<see cref="IStudentDirectory"/>
/// — the same resolution the sign command handler uses), NOT via the token/row's
/// <c>WardStudentId</c> (P1-3 adjudication: the recipient row is <c>FirstOrDefault</c> per
/// (assignment, contact), so a <c>WardStudentId == routeStudentId</c> predicate would 403
/// legitimate multi-ward sign-offs). Rejects with 401 for token failure and 403 for
/// scope/row/link mismatch. The endpoint delegate runs INSIDE the explicit-tenant scope so
/// handlers + the tenant EF filter see the token's tenant (P1-2: previously it ran after
/// the scope exited, so on the public group <see cref="TenantProvider"/> fell to
/// Guid.Empty). On success stashes the resolved acting <see cref="GuardianId"/> (from
/// the recipient's <c>OwnerId</c>) in <see cref="HttpContext.Items"/> for the endpoint to
/// thread into the shared sign command. No new keyring, no new purpose, no OIDC dependency.
/// </summary>
public sealed class GuardianTokenEndpointFilter(
    DeepLinkProtector protector,
    ITenantContextAccessor tenantAccessor,
    ISubmissionRepository submissionRepository,
    IStudentDirectory studentDirectory,
    IFeatureFlagService featureFlags,
    ILogger<GuardianTokenEndpointFilter> logger) : IEndpointFilter
{
    /// <summary>The <see cref="HttpContext.Items"/> key holding the resolved acting guardian id.</summary>
    public const string GuardianIdItemKey = "GuardianId";

    /// <inheritdoc/>
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // Feature-flag kill-switch (decision (f)/to-verify 4): the same
        // FEATURE:EnableDeepLinks dark-launch posture as the landing. Flag off →
        // 401 on the guardian group.
        if (!await featureFlags.IsEnabledAsync(FeatureFlagKeys.EnableDeepLinks, http.RequestAborted))
        {
            logger.LogInformation("Guardian route group: EnableDeepLinks is OFF — rejecting");
            return Results.Unauthorized();
        }

        if (!http.Request.Headers.TryGetValue("x-deeplink-token", out var headerValues)
            || string.IsNullOrWhiteSpace(headerValues.ToString()))
        {
            logger.LogInformation("Guardian route group: missing x-deeplink-token header");
            return Results.Unauthorized();
        }

        var token = headerValues.ToString()!;
        var payload = protector.TryUnprotect(token);
        if (payload is null || payload.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            logger.LogInformation("Guardian route group: invalid, tampered, or expired x-deeplink-token");
            return Results.Unauthorized();
        }

        // Route-scope resolution — the token must target the assignment in the URL.
        var routeAssignmentId = Guid.Empty;
        if (http.Request.RouteValues.TryGetValue("id", out var idValue) &&
            Guid.TryParse(idValue as string, out var parsedId))
        {
            routeAssignmentId = parsedId;
        }
        if (routeAssignmentId != Guid.Empty && routeAssignmentId != payload.AssignmentId)
        {
            logger.LogInformation(
                "Guardian route group: token assignment {TokenAssignment} != route {Route}",
                payload.AssignmentId, routeAssignmentId);
            return Forbidden();
        }
        var assignmentId = payload.AssignmentId;

        // The route student the guardian is acting for (the URL's {studentId}).
        var routeStudentId = Guid.Empty;
        if (http.Request.RouteValues.TryGetValue("studentId", out var sv) &&
            Guid.TryParse(sv as string, out var parsedStudent))
        {
            routeStudentId = parsedStudent;
        }

        // Resolve the acting guardian from the authoritative recipient row, authorize it for
        // the route student via the guardian link, and run the endpoint INSIDE the token's
        // tenant scope (P1-2): tenants + the EF per-entity filter must see the validated
        // tenant, which the public group would otherwise leave at Guid.Empty. Tenant mismatch
        // ⇒ row absent ⇒ 403.
        return await tenantAccessor.RunWithExplicitTenantAsync(
            payload.TenantId,
            async ct =>
            {
                var recipient = await submissionRepository.GetRecipientAsync(
                    assignmentId, payload.ContactId, ct);
                if (recipient is null)
                {
                    logger.LogInformation(
                        "Guardian route group: no recipient row for assignment {AssignmentId}/contact {ContactId}",
                        assignmentId, payload.ContactId);
                    return Forbidden();
                }

                if (recipient.TenantId != payload.TenantId)
                {
                    logger.LogInformation(
                        "Guardian route group: recipient tenant {RowTenant} != token tenant {TokenTenant}",
                        recipient.TenantId, payload.TenantId);
                    return Forbidden();
                }

                if (recipient.OwnerType != ContactOwnerType.Guardian)
                {
                    logger.LogInformation(
                        "Guardian route group: recipient for contact {ContactId} is not guardian-owned",
                        payload.ContactId);
                    return Forbidden();
                }

                // Guardian-link authorization (P1-3 adjudication): the token-resolved guardian
                // must be a linked guardian of the ROUTE student. NOT a WardStudentId equality
                // check — the recipient row is FirstOrDefault per (assignment, contact), so a
                // WardStudentId predicate would reject legitimate multi-ward sign-offs. GET
                // context/certificate reads are authorized here; the POST additionally keeps
                // the sign command's fail-closed guardian check as defense-in-depth.
                if (routeStudentId == Guid.Empty ||
                    !await studentDirectory.IsGuardianOfAsync(routeStudentId, recipient.OwnerId, ct))
                {
                    logger.LogInformation(
                        "Guardian route group: guardian {GuardianId} is not a linked guardian of route student {RouteStudent}",
                        recipient.OwnerId, routeStudentId);
                    return Forbidden();
                }

                // Authoritative server-side expiry — a re-minted short-TTL header token
                // cannot outlive the stored DeepLinkExpiresAt (decision (a) res. 3).
                if (recipient.DeepLinkExpiresAt is not { } stored ||
                    stored <= DateTimeOffset.UtcNow)
                {
                    logger.LogInformation(
                        "Guardian route group: stored DeepLinkExpiresAt {Stored} is expired or null for assignment {AssignmentId}",
                        recipient.DeepLinkExpiresAt, assignmentId);
                    return Forbidden();
                }

                http.Items[GuardianIdItemKey] = recipient.OwnerId;
                // Run the endpoint (and its handlers + EF queries) under the explicit tenant.
                return await next(context);
            });
    }

    /// <summary>
    /// Scope/row rejection. Uses a bare 403 rather than <c>Results.Forbid()</c>:
    /// the guardian group carries no authentication scheme (the token is the
    /// credential), so there is nothing to challenge, and a plain status keeps the
    /// filter free of an <c>IAuthenticationService</c> dependency.
    /// </summary>
    private static IResult Forbidden() => Results.StatusCode(StatusCodes.Status403Forbidden);
}
