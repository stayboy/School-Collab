using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Api;

public static class SignatureConsentTextEndpoints
{
    /// <summary>
    /// Maps the per-tenant guardian sign-off consent text under
    /// <c>/api/settings/signature-consent-text</c>. Cookie-gated when OIDC is on; open
    /// under TestAuth in dev (the tenant context is still enforced by the tenant
    /// query filter, so reads/writes stay scoped to the caller's tenant). WS-C2.
    /// </summary>
    public static WebApplication MapSignatureConsentTextEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var group = app.MapGroup("/api/settings");

        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            group.RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
        }

        group.MapSignatureConsentTextRoutes();

        return app;
    }
}
