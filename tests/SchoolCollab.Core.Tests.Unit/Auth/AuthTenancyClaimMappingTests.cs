using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tests.Unit.Auth;

/// <summary>
/// ar-20 claim-mapping test. The <c>RealBearerPath</c> test drives the REAL
/// <see cref="JwtBearerHandler"/> (an <c>AddJwtBearer</c> scheme with no <c>Authority</c>, so no
/// metadata fetch) over a hand-signed JWT carrying <c>tenant_id</c> / <c>teacher_id</c>, then
/// asserts both (i) the mapped principal carries those claims and (ii) <see cref="ICurrentUser"/>
/// resolves teacher id + tenant from that principal. This is the non-vacuous guard: if the bearer
/// scheme wiring or the token-validation parameters were broken, this test fails because the
/// handler would not produce a successful <see cref="AuthenticateResult"/> or the claims would be
/// missing. The TestAuth shape is compared for parity so the two dev/CI surfaces stay identical.
/// </summary>
[TestClass]
public class AuthTenancyClaimMappingTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private const string Issuer = "https://keycloak.local/realms/school-collab";
    private const string Audience = "school-collab-client";
    private static readonly SymmetricSecurityKey SigningKey =
        new("dev-only-signing-key-that-is-at-least-32-bytes-!!"u8.ToArray());

    [TestMethod]
    public async Task RealBearerPath_JwtBearerHandler_MapsClaims_And_CurrentUserResolves()
    {
        // Hand-signed JWT that would be rejected unless the AddJwtBearer scheme actually
        // validates the token against the pinned TokenValidationParameters (issuer, audience,
        // symmetric key) below.
        var token = new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(
                Issuer, Audience,
                BaseClaims(),
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256)));

        // Register the REAL bearer scheme with NO Authority — the handler validates purely against
        // the explicit TokenValidationParameters, so no discovery/metadata fetch is attempted in CI.
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Issuer,
                    ValidAudience = Audience,
                    IssuerSigningKey = SigningKey,
                };
            });
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = sp;
        httpContext.Request.Headers.Authorization = "Bearer " + token;

        var result = await sp.GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(httpContext, JwtBearerDefaults.AuthenticationScheme);

        result.Succeeded.Should().BeTrue("the real JwtBearer handler must validate the hand-signed token");

        var principal = result.Principal!;
        foreach (var name in new[] { "tenant_id", "tenant_name", "tenant_type", "teacher_id" })
            principal.FindFirst(name).Should().NotBeNull($"claim {name} must be mapped");
        principal.FindFirst("tenant_id")!.Value.Should().Be(TenantId.ToString());
        principal.FindFirst("teacher_id")!.Value.Should().Be(TeacherId.ToString());

        // UseAuthentication middleware sets context.User from the AuthenticateResult; since we
        // invoked IAuthenticationService directly, attach it the same way so CurrentUser reads it.
        httpContext.User = principal;
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var currentUser = new CurrentUser(accessor, new TenantProvider(accessor));
        currentUser.IsAuthenticated.Should().BeTrue();
        currentUser.TeacherId.Should().Be(TeacherId);
        currentUser.CurrentTenant.TenantId.Should().Be(TenantId);
    }

    /// <summary>TestAuth shape resolves identically to the bearer shape through <see cref="ICurrentUser"/>.</summary>
    [TestMethod]
    public void TestAuthShape_Resolves_Identically_To_Bearer_Shape()
    {
        var bearerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new JwtSecurityTokenHandler().ReadJwtToken(
                new JwtSecurityTokenHandler().WriteToken(
                    new JwtSecurityToken(
                        Issuer, Audience, BaseClaims(), notBefore: DateTime.UtcNow,
                        expires: DateTime.UtcNow.AddHours(1)))).Claims, "Bearer"));

        var testAuthPrincipal = new ClaimsPrincipal(
            new ClaimsIdentity(BaseClaims(), "TestAuth"));

        var bearerUser = Build(bearerPrincipal);
        var testAuthUser = Build(testAuthPrincipal);

        bearerUser.TeacherId.Should().Be(TeacherId);
        testAuthUser.TeacherId.Should().Be(TeacherId);
        bearerUser.CurrentTenant.TenantId.Should().Be(TenantId);
        testAuthUser.CurrentTenant.TenantId.Should().Be(TenantId);
    }

    private static ICurrentUser Build(ClaimsPrincipal principal)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new CurrentUser(accessor, new TenantProvider(accessor));
    }

    private static IEnumerable<Claim> BaseClaims() =>
    [
        new(ClaimTypes.NameIdentifier, "test-user"),
        new(ClaimTypes.Name, "Test User"),
        new("tenant_id", TenantId.ToString()),
        new("tenant_name", "Dev School"),
        new("tenant_type", "School"),
        new("teacher_id", TeacherId.ToString()),
    ];
}
