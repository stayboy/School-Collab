using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tests.Unit.Auth;

/// <summary>
/// ar-20 <see cref="ICurrentUser"/> claim-shape tests. Proves that the same
/// <c>tenant_id</c>/<c>tenant_name</c>/<c>tenant_type</c>/<c>teacher_id</c> claims
/// resolve identically whether the principal came from an (a) OIDC cookie, (b) bearer
/// JWT, or (c) TestAuth identity — and that a principal without <c>teacher_id</c> yields
/// <c>TeacherId == null</c> without throwing.
/// </summary>
[TestClass]
public class CurrentUserTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000003");

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
    ];

    // (a) OIDC-cookie shape — ClaimActions.MapJsonKey emits claims whose Type is the json
    // key ("tenant_id", "teacher_id", …). The cookie identity carries these verbatim.
    [TestMethod]
    public void TeacherId_FromOidcCookieClaimShape()
    {
        var user = Build(new ClaimsPrincipal(new ClaimsIdentity(
            BaseClaims().Append(new Claim("teacher_id", TeacherId.ToString())), "OIDC")));
        user.IsAuthenticated.Should().BeTrue();
        user.TeacherId.Should().Be(TeacherId);
        user.CurrentTenant.TenantId.Should().Be(TenantId);
    }

    // (b) bearer-JWT shape — decode a real (unsigned) access token and map its claims the
    // way JwtBearer's default inbound mapping does (custom claims keep their type verbatim).
    [TestMethod]
    public void TeacherId_FromBearerJwtClaimShape()
    {
        var jwtClaims = BaseClaims().Append(new Claim("teacher_id", TeacherId.ToString()));
        var token = new JwtSecurityToken(
            "https://keycloak.local/realms/school-collab", "school-collab-client",
            jwtClaims, notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddHours(1));
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        var decodedClaims = new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims;

        var principal = new ClaimsPrincipal(new ClaimsIdentity(decodedClaims, "Bearer"));
        var user = Build(principal);
        user.TeacherId.Should().Be(TeacherId);
        user.CurrentTenant.TenantId.Should().Be(TenantId);
    }

    // (c) TestAuth shape — exactly the claims TestAuthHandler emits.
    [TestMethod]
    public void TeacherId_FromTestAuthClaimShape()
    {
        var claims = BaseClaims().Append(new Claim("teacher_id", TeacherId.ToString()));
        var user = Build(new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")));
        user.TeacherId.Should().Be(TeacherId);
        user.CurrentTenant.TenantId.Should().Be(TenantId);
    }

    // No teacher_id ⇒ TeacherId == null, never throws.
    [TestMethod]
    public void NoTeacherClaim_TeacherIdIsNull_NeverThrows()
    {
        var user = Build(new ClaimsPrincipal(new ClaimsIdentity(BaseClaims(), "OIDC")));
        user.TeacherId.Should().BeNull();
        user.CurrentTenant.TenantId.Should().Be(TenantId); // tenant still resolves
    }
}
