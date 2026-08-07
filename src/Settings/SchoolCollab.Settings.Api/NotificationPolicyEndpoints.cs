using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Api;

public static class NotificationPolicyEndpoints
{
    /// <summary>
    /// Maps the per-tenant global-default notification policy under
    /// <c>/api/settings/notification-policy</c>. Cookie-gated when OIDC is on; open
    /// under TestAuth in dev (the tenant context is still enforced by the tenant
    /// query filter, so reads/writes stay scoped to the caller's tenant).
    /// </summary>
    public static WebApplication MapNotificationPolicyEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var group = app.MapGroup("/api/settings");

        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            group.RequireAuthorization();
        }

        group.MapNotificationPolicyRoutes();

        return app;
    }
}
