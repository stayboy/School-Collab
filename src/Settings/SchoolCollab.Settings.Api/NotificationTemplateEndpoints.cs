using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Api;

public static class NotificationTemplateEndpoints
{
    /// <summary>
    /// Maps the tenant notification-template store under
    /// <c>/api/settings/notification-templates</c>. Cookie-gated when OIDC is on; open
    /// under TestAuth in dev (the tenant context is still enforced by the tenant
    /// query filter, so reads/writes stay scoped to the caller's tenant).
    /// </summary>
    public static WebApplication MapNotificationTemplateEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var group = app.MapGroup("/api/settings");

        group.RequireAuthIfOidc(featureFlags);

        group.MapNotificationTemplateRoutes();

        return app;
    }
}
