using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.Tenants.Commands.SetDefaultTenant;
using SchoolCollab.Settings.Core.CQRS.Tenants.Commands.SetUserDefaultTenant;
using SchoolCollab.Settings.Core.CQRS.Tenants.Queries.GetUserDefaultTenant;
using SchoolCollab.Settings.Core.CQRS.Tenants.Queries.ListTenants;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

/// <summary>
/// Routes for the global tenant registry and the admin portal's default-tenant
/// designation.
/// </summary>
/// <remarks>
/// <para>
/// Two distinct things live here and they are deliberately separate endpoints:
/// the <b>platform default</b> (a property of one tenant, visible to everyone, written
/// through <c>/{id}/default</c>) and an operator's <b>personal preference</b> (keyed by
/// the caller's own <c>sub</c> claim, written through <c>/me/default</c>).
/// </para>
/// <para>
/// The <c>/me</c> routes take the subject from the bound <see cref="ClaimsPrincipal"/>
/// — never from the request body — so one operator cannot read or write another's
/// preference. That also keeps <c>ICurrentUser</c>'s shape unchanged.
/// </para>
/// </remarks>
public static class TenantRoutes
{
    public static RouteGroupBuilder MapTenantRoutes(this RouteGroupBuilder group)
    {
        // List every tenant (global registry) for the dev tenant switcher dropdown.
        // IsDefault on each row tells the portal which tenant it should fall back to.
        group.MapGet("/", async Task<Ok<TenantDto[]>> (
            [FromServices] IQueryHandler<ListTenants, TenantDto[]> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListTenants(), ct)));

        // Designate the platform's default tenant for the admin portal.
        group.MapPut("/{id:guid}/default", async Task<Results<NoContent, NotFound>> (
            Guid id,
            [FromServices] ICommandHandler<SetDefaultTenant> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SetDefaultTenant(id), ct);
                return TypedResults.NoContent();
            }
            catch (TenantNotFoundException)
            {
                return TypedResults.NotFound();
            }
        });

        // The caller's own recorded preference. Always 200: a missing preference is the
        // documented "none recorded" state (null DefaultTenantId), not a 404 — the portal
        // then falls through to the platform default.
        group.MapGet("/me/default", async Task<Ok<UserTenantPreferenceDto>> (
            ClaimsPrincipal user,
            [FromServices] IQueryHandler<GetUserDefaultTenant, UserTenantPreferenceDto> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetUserDefaultTenant(SubjectOf(user)), ct)));

        // Record or clear the caller's own preference. A null TenantId clears it; Guid.Empty
        // records an explicit preference for the system / no-tenant context.
        // Four outcome arms — NoContent, Unauthorized, NotFound, BadRequest<MessageResponse> — which a
        // Results<…> union would not keep readable, so this stays IResult by decision D19.
        group.MapPut("/me/default", async Task<IResult> (
            ClaimsPrincipal user,
            [FromBody] SetUserDefaultTenantRequest req,
            [FromServices] ICommandHandler<SetUserDefaultTenant> handler,
            CancellationToken ct) =>
        {
            var subject = SubjectOf(user);
            if (subject.Length == 0)
            {
                // No principal ⇒ nothing to key a preference to. The OIDC branch of
                // MapTenantEndpoints already requires authentication; this covers the
                // TestAuth-off/dev edge and any future anonymous mount.
                return TypedResults.Unauthorized();
            }

            try
            {
                await handler.HandleAsync(new SetUserDefaultTenant(subject, req.TenantId), ct);
                return TypedResults.NoContent();
            }
            catch (TenantNotFoundException)
            {
                return TypedResults.NotFound();
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest(new MessageResponse(ex.Message));
            }
        });

        return group;
    }

    /// <summary>Body of <c>PUT /api/tenants/me/default</c>.</summary>
    /// <param name="TenantId">
    /// Null clears the preference; <see cref="Guid.Empty"/> selects the system / no-tenant
    /// context; any other value must name a registry tenant.
    /// </param>
    public sealed record SetUserDefaultTenantRequest(Guid? TenantId);

    /// <summary>
    /// The authenticated principal's subject identifier. <see cref="ClaimTypes.NameIdentifier"/>
    /// is the inbound-mapped form of the OIDC <c>sub</c> claim (the mapping is on by default and
    /// the same claim shape travels on the TestAuth principal), so both auth modes resolve here.
    /// </summary>
    private static string SubjectOf(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
}
