using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ResolveShortLink;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Api.Endpoints;

/// <summary>
/// Round notification-templates-b2a (spec §3.6, Q61) — the public short-link resolution specialty.
/// Mapped OUTSIDE the teacher/admin-gated <c>/assignments</c> group with <b>no</b>
/// <c>RequireAuthIfOidc</c>: the code IS the credential (an unauthenticated visitor arrives from an
/// SMS), so the route is deliberately <b>anonymous and cross-tenant</b> — there is no principal to
/// check and no tenant to scope by. The Families host's public <c>/l/{code}</c> route is the only
/// caller, through its own tenant-less cross-module client (never the tenant-propagating
/// <c>FamiliesApiClient</c>, which cannot serve a tenant-less visitor).
/// <para>The privilege this buys is bounded by the handler: one indexed single-row read on the
/// globally unique <c>ShortLinkCode</c> that returns the deep-link token and nothing else.</para>
/// </summary>
public static class ShortLinkRoutes
{
    /// <summary>Maps the anonymous <c>/{code}</c> resolution read on the supplied group.</summary>
    public static RouteGroupBuilder MapShortLinkResolutionRoutes(this RouteGroupBuilder group)
    {
        // 404 (not 410) for an unknown/expired code: the Families landing turns both into the same
        // friendly expired page, and the reply never says which of the two it was.
        // Explicitly anonymous: the group itself has no policy, but the route states it so the
        // composition guard and the reader never mistake this for an auth-gated endpoint.
        group.MapGet("/{code}", async Task<Results<Ok<ShortLinkResolutionDto>, NotFound>> (
            string code,
            [FromServices] IQueryHandler<ResolveShortLink, ShortLinkResolutionDto?> handler,
            CancellationToken ct) =>
        {
            var resolution = await handler.HandleAsync(new ResolveShortLink(code), ct);
            return resolution is null ? TypedResults.NotFound() : TypedResults.Ok(resolution);
        })
        .AllowAnonymous();

        return group;
    }
}
