using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// The Q3 wire-spelling **contract pin** (round <c>portal-submission-grade</c> AC7(a)): the auth
/// service's two portal-facing session bodies serialize with EXACTLY the camelCase key set the
/// Python portal parses — nothing renamed, nothing added, nothing dropped.
///
/// <para>
/// This is a <b>drift-detection pin, not a pre-change failing test</b>: the spelling it asserts
/// is already correct (the previous round's portal tests drive the real client over a hand-written
/// camelCase body, which is what proved it). Its value is that the two halves of the contract now
/// have one automated pin each — a serialization-policy change here, or a parse change in the
/// portal, reddens exactly one of them instead of silently blanking a page. The Python half lives
/// in <c>src/SchoolCollab.Portals/tests/test_teacher_session.py</c>; neither side can be satisfied
/// by the other's tolerance.
/// </para>
///
/// <para>
/// The bodies are read from the REAL pipeline (<see cref="AuthEndpointTestHost"/>, the composed
/// Kestrel host) — the same host the session endpoint tests use — so the assertion observes
/// ASP.NET's own serialization, never a re-implementation of it in the test.
/// </para>
/// </summary>
[TestClass]
public class SessionWireSpellingTests
{
    private const string AccessToken = "wire_spelling_access_token";
    private const string RefreshToken = "wire_spelling_refresh_token";

    /// <summary>The D18 read body's pinned key set — the portal's <c>SessionData</c> fields.</summary>
    private static readonly string[] SessionReadKeys =
    [
        "sessionId",
        "tenantId",
        "tenantName",
        "tenantType",
        "teacherId",
        "roles",
        "expiresInSeconds",
    ];

    /// <summary>The D2 claims body's pinned key set — <c>SessionResponse</c> minus the session and
    /// lifetime fields (the remote host's portal-session authentication reads exactly these).</summary>
    private static readonly string[] SessionClaimsKeys =
    [
        "tenantId",
        "tenantName",
        "tenantType",
        "teacherId",
        "roles",
    ];

    private static string SeedSession(AuthEndpointTestHost host) =>
        host.Services.GetRequiredService<PortalSessionStore>()
            .Create(
                AccessToken,
                RefreshToken,
                SessionEndpointTests.JwtWithClaims(tenantName: "Wire School", roles: ["teacher"]),
                3600);

    private static async Task<string> ReadBodyAsync(AuthEndpointTestHost host, string path)
    {
        var response = await host.Client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} answers the claim set as data");
        return await response.Content.ReadAsStringAsync();
    }

    private static string[] KeySet(string body)
    {
        using var document = JsonDocument.Parse(body);
        return [.. document.RootElement.EnumerateObject().Select(property => property.Name).Order()];
    }

    [TestMethod]
    public async Task SessionReadBody_CarriesExactlyThePinnedCamelCaseKeySet()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var sessionId = SeedSession(host);

        var body = await ReadBodyAsync(host, $"/auth/session/{sessionId}");

        // Exact set equality, not "contains": a renamed or removed key is a drift the portal's
        // tolerant parser would otherwise absorb as a blank field (and an ADDED key is a
        // contract widening the portal knows nothing about).
        KeySet(body).Should().Equal(SessionReadKeys.Order(),
            "the D18 read body's spelling IS the portal's contract (round portal-session-adoption "
            + "Q3): the previous round pinned it by hand in another context's test — this is the "
            + "one-way pin on the serializing side.");

        // The snake_case claim names are the D9 CLAIM contract, never the wire spelling.
        body.Should().NotContain("tenant_id").And.NotContain("teacher_id")
            .And.NotContain("session_id").And.NotContain("expires_in_seconds");
    }

    [TestMethod]
    public async Task SessionClaimsBody_CarriesExactlyThePinnedCamelCaseKeySet()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var sessionId = SeedSession(host);

        var body = await ReadBodyAsync(host, $"/auth/session/{sessionId}/claims");

        KeySet(body).Should().Equal(SessionClaimsKeys.Order(),
            "the remote portal-session claims read is the OTHER half of the same wire contract: "
            + "the Assignments API's PortalSessionClaimsReader parses exactly these keys (pinned "
            + "in PortalSessionReaderPolicyTests), so this side must not drift either.");

        body.Should().NotContain("tenant_id").And.NotContain("teacher_id");
    }
}
