using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Services;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// The portal-session authentication scheme's HOST-LEVEL tests (spec D12/D15): a live custody
/// session authenticates the portal's own admin surface and the failure modes fail closed with a
/// bare 401. The scheme itself lives in the shared kernel (round <c>portal-session-adoption</c>
/// D19/D1); its in-process handler tests — the fail-closed contract, the claim materialization and
/// the challenge — moved with it to <c>tests/SchoolCollab.Core.Tests.Unit/Auth/</c>, which is where
/// they can be run against a stubbed claims port instead of real custody.
/// </summary>
[TestClass]
public class PortalSessionAuthenticationHandlerTests
{
    private const string AccessToken = "seeded_access_token";
    private const string RefreshToken = "seeded_refresh_token";

    [TestMethod]
    public async Task PortalSession_WithUserAdminRole_AuthorizesTheAdminSurface()
    {
        // THE POINT of the policy attachment (AuthEndpointGroup row): the admin UI lives in the
        // portal, which presents only its opaque session id — no token anywhere (AC11).
        await using var host = await AuthEndpointTestHost.StartAsync();
        StubAdmin(host);
        var sessionId = SeedHostSession(host, roles: ["user-admin"]);

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/admin/users");
        request.Headers.Add(PortalSessionAuthenticationHandler.SessionHeaderName, sessionId);
        var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("alice");
    }

    [TestMethod]
    public async Task PortalSession_WithoutUserAdminRole_Is403()
    {
        // The gate is still a ROLE gate: a portal session that is merely authenticated must not
        // reach the admin surface. This half is what stops the pair passing vacuously.
        await using var host = await AuthEndpointTestHost.StartAsync();
        var sessionId = SeedHostSession(host, roles: ["teacher"]);

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/admin/users");
        request.Headers.Add(PortalSessionAuthenticationHandler.SessionHeaderName, sessionId);
        var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task PortalSession_UnknownSession_Is401()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/admin/users");
        request.Headers.Add(PortalSessionAuthenticationHandler.SessionHeaderName, "not-a-live-session");
        var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
    }

    /// <summary>Seeds a live custody session bearing the D9 claim contract with the given realm
    /// roles, and returns its opaque id.</summary>
    private static string SeedHostSession(AuthEndpointTestHost host, string[] roles) =>
        host.Services.GetRequiredService<PortalSessionStore>()
            .Create(AccessToken, RefreshToken, SessionEndpointTests.JwtWithClaims("Dev School", roles), 3600);

    /// <summary>Routes the admin REST client's token fetch to a canned success and the users list
    /// to one canned user (any other call fails loudly rather than touching the network).</summary>
    private static void StubAdmin(AuthEndpointTestHost host) => host.AdminStub.OnSendAsync = (request, _) =>
        Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/protocol/openid-connect/token", StringComparison.Ordinal)
            ? SessionEndpointTests.Json(
                System.Net.HttpStatusCode.OK, """{"access_token":"admin-bearer-token","expires_in":3600}""")
            : request.RequestUri.AbsolutePath.EndsWith("/users", StringComparison.Ordinal)
                ? SessionEndpointTests.Json(
                    System.Net.HttpStatusCode.OK, """[{"id":"u1","username":"alice","enabled":true}]""")
                : new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway));
}
