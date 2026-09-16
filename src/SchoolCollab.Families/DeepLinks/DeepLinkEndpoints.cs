using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SchoolCollab.Families.DeepLinks;

/// <summary>
/// WS-E1 (ar-14-deep-links) — the repository's FIRST public token-auth route
/// group (decision (c)). Mapped OUTSIDE <c>MapRazorComponents</c> (which is
/// <c>RequireAuthorization</c>'d when OIDC is on) and with NO
/// <c>RequireAuthorization</c>: the bearer token in the URL is itself the
/// credential. A valid link (flag on) signs the guardian into a cookie principal
/// carrying <c>tenant_id</c> (bridged to <see cref="SchoolCollab.Core.Tenancy.ITenantProvider"/>
/// by <see cref="SchoolCollab.Core.Auth.TenantClaimsTransformation"/>) and redirects to the
/// ward surface; an expired / tampered / dark-launch-false link redirects to the
/// friendly <c>LinkExpired</c> page (the flow emits a 302 → 200 redirect to that page) —
/// never a raw error.
/// </summary>
public static class DeepLinkEndpoints
{
    /// <summary>Maps the public <c>/deeplink/{token}</c> landing route.</summary>
    public static WebApplication MapDeepLinkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/deeplink");

        group.MapGet("/{token}", async (
            string token,
            HttpContext httpContext,
            DeepLinkLandingService landing,
            ILogger<DeepLinkLandingService> logger,
            CancellationToken ct) =>
        {
            var result = await landing.HandleAsync(token, ct: ct);
            if (result.Outcome != DeepLinkLandingOutcome.Success)
            {
                // Expired / tampered / flag-off — friendly expired page, no sign-in.
                return Results.Redirect("/deeplink/expired");
            }

            await SignInAsync(httpContext, result);
            logger.LogInformation(
                "Deep-link landing succeeded for tenant {TenantId}, redirecting to {Target}",
                result.TenantId, result.RedirectUrl);
            return Results.Redirect(result.RedirectUrl!);
        });

        return app;
    }

    private static async Task SignInAsync(HttpContext httpContext, DeepLinkLandingResult result)
    {
        if (result.TenantId is not { } tenantId)
        {
            return;
        }

        var contactId = result.ContactId ?? Guid.Empty;

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, contactId.ToString()),
                new Claim("contact_id", contactId.ToString()),
                new Claim("tenant_id", tenantId.ToString())
            },
            CookieAuthenticationDefaults.AuthenticationScheme);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }
}
