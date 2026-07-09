using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// Test-only authentication handler that auto-authenticates every request
/// as a known tenant user. Registered automatically by
/// <see cref="AuthTenancyExtensions.AddAuthAndTenancy"/> when the
/// hosting environment is named "Testing".
/// </summary>
public class TestAuthHandlerOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The tenant ID to use when no tenant is explicitly selected via the dev
    /// tenant switcher. Defaults to <see cref="Guid.Empty"/> (no tenant) so that
    /// the per-tenant UI is hidden until a real tenant is selected. Tests can
    /// override this to simulate a specific tenant context.
    /// </summary>
    public Guid TenantId { get; set; } = Guid.Empty;
}

public sealed class TestAuthHandler : AuthenticationHandler<TestAuthHandlerOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<TestAuthHandlerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Default: use the configured TenantId (Guid.Empty unless tests override it).
        // When IDevTenantSelection is registered (auth-disabled / TestAuth mode),
        // the dev tenant switcher can override this with a user-selected tenant.
        var tenantId = Options.TenantId;

        // Prefer the x-tenant-id header propagated by the admin shell's
        // HttpClient (see TenantPropagationDelegatingHandler). This is
        // topology-independent and works even when the shared Redis cache
        // (IDevTenantSelection) is unavailable. Only honored in TestAuth (dev)
        // mode, so it cannot be spoofed in production OIDC.
        var headerTenant = Context.Request.Headers["x-tenant-id"].ToString();
        if (Guid.TryParse(headerTenant, out var headerTenantId) && headerTenantId != Guid.Empty)
        {
            tenantId = headerTenantId;
        }
        else
        {
            // Dev tenant switcher (auth-disabled only): if the admin shell's
            // DevTenantSwitcher has selected a real tenant, use it instead of the
            // default so this host's tenant query filters resolve the selected
            // tenant's data and coded-value overrides. Resolved per-request from the
            // HTTP request scope; if IDevTenantSelection isn't registered, falls back
            // to Options.TenantId.
            var devSelection = Context.RequestServices.GetService<IDevTenantSelection>();
            if (devSelection is not null)
            {
                var selected = await devSelection.GetSelectedTenantIdAsync(Context.RequestAborted);

                // DEBUG: Log tenant selection from Redis
                Logger.LogDebug("TestAuthHandler: IDevTenantSelection returned {SelectedTenantId} (Options.TenantId={OptionsTenantId})",
                    selected?.ToString() ?? "null", Options.TenantId);

                if (selected.HasValue)
                    tenantId = selected.Value;
                // If selected is null, the user cleared the selection (selected
                // "(default tenant)" in the switcher) → use Options.TenantId which
                // defaults to Guid.Empty (no tenant).
            }
            else
            {
                Logger.LogDebug("TestAuthHandler: No IDevTenantSelection registered, using Options.TenantId={TenantId}", tenantId);
            }
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("tenant_name", "Test Tenant"),
            new Claim("tenant_type", "School"),
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}

/// <summary>
/// Extension to register the test auth handler in integration-test host builders.
/// </summary>
public static class TestAuthExtensions
{
    public const string TestAuthScheme = "TestAuth";

    public static AuthenticationBuilder AddTestAuth(
        this AuthenticationBuilder builder,
        Action<TestAuthHandlerOptions>? configure = null)
    {
        return builder.AddScheme<TestAuthHandlerOptions, TestAuthHandler>(
            TestAuthScheme, configure ?? (_ => { }));
    }
}
