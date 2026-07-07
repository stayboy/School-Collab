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
    /// The tenant ID the test user will be associated with.
    /// Defaults to a well-known test GUID.
    /// </summary>
    public Guid TenantId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
}

internal sealed class TestAuthHandler : AuthenticationHandler<TestAuthHandlerOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<TestAuthHandlerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Default to the configured test tenant.
        var tenantId = Options.TenantId;

        // Dev tenant switcher (auth-disabled only): if the admin shell's
        // DevTenantSwitcher has selected a real tenant, use it instead of the
        // fixed default so this host's tenant query filters resolve the selected
        // tenant's data and coded-value overrides. Resolved per-request from the
        // HTTP request scope; if IDevTenantSelection isn't registered or no
        // tenant is selected, falls back to Options.TenantId.
        var devSelection = Context.RequestServices.GetService<IDevTenantSelection>();
        if (devSelection is not null)
        {
            var selected = await devSelection.GetSelectedTenantIdAsync(Context.RequestAborted);
            if (selected.HasValue)
                tenantId = selected.Value;
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
