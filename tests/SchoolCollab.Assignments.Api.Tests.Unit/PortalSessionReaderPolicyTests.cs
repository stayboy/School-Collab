using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Constants;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// The reader policy's portal-session adoption (round <c>portal-session-adoption</c> D4/D19,
/// AC1/AC2): <c>RequireAssignmentReader</c> names the portal-session GATEWAY — so a teacher
/// presenting a live session id in <c>X-Portal-Session</c> authorizes with the SAME D9 claim set
/// the cookie/bearer paths carry, an unknown session fails closed to a BARE 401 (never a 500, never
/// a redirect), and existing bearer callers authorize exactly as before through the pinned
/// fallback. Host: the container-free TestServer harness (the
/// <c>AssignmentReaderPolicyRouteTests</c> shape), with the gateway registered and the claims port
/// stubbed exactly as <c>Program.cs</c> wires them. The last two cases drop the host and drive the
/// REAL <see cref="PortalSessionClaimsReader"/> over a scripted handler: they pin the D2 body's
/// camelCase **wire spelling** against the reader's parse — the half of the contract that was
/// pinned only by hand-written assertions in another context's test until here.
/// </summary>
[TestClass]
public class PortalSessionReaderPolicyTests
{
    private static readonly Guid TeacherClaimId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GradeId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>The canned claim set the stub claims port answers for its session id — the D9
    /// contract the portal-session handler materializes, teacher role included.</summary>
    private static readonly PortalClaims TeacherClaims =
        new("00000000-0000-0000-0000-000000000002", "Dev School", "School", TeacherClaimId.ToString(), [RealmRoleNames.Teacher]);

    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => false;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? TeacherId => TeacherClaimId;
        public TenantContext CurrentTenant => new(Guid.Empty, "Test", TenantType.School);
    }

    private sealed class StubScopeProvider : ITeacherScopeProvider
    {
        public Task<TeacherScope> GetScopeAsync(Guid teacherId, CancellationToken cancellationToken = default)
            => Task.FromResult(TeacherScope.ForTeacher(teacherId, [new TeacherSubjectGrade(GradeId, null, null)]));
    }

    private sealed class StubListHandler : IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]>
    {
        public Task<AssignmentSummaryDto[]> HandleAsync(ListAssignmentsQuery query, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<AssignmentSummaryDto>());
    }

    /// <summary>The bearer scheme, authenticating per request from headers (the
    /// <c>AssignmentReaderPolicyRouteTests.HeaderPrincipalAuthHandler</c> shape): a bearer-presenting
    /// caller's roles ride <c>x-test-roles</c>.</summary>
    private sealed class HeaderPrincipalAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var roles = Request.Headers["x-test-roles"].ToString();
            if (string.IsNullOrWhiteSpace(roles))
            {
                // No roles header ⇒ no presented credential: the anonymous case must fail
                // authentication (401), never authenticate a role-less principal (403).
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "test-user") };
            foreach (var role in roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    /// <summary>The claims port the moved portal-session handler authenticates through on this
    /// host — canned per test, so one host serves the live-session and dead-session cases.</summary>
    private sealed class StubClaimsReader : IPortalSessionClaimsReader
    {
        public PortalClaims? NextClaims { get; set; } = TeacherClaims;

        public string? LastSessionId { get; private set; }

        public ValueTask<PortalClaims?> ReadClaimsAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            LastSessionId = sessionId;
            return ValueTask.FromResult(NextClaims);
        }
    }

    private static async Task<WebApplication> StartHostAsync(StubClaimsReader? claimsReader = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        builder.Services.AddAuthentication(AuthTenancyExtensions.BearerScheme)
            .AddScheme<AuthenticationSchemeOptions, HeaderPrincipalAuthHandler>(
                AuthTenancyExtensions.BearerScheme, _ => { })
            .AddPortalSessionAuthentication(AuthTenancyExtensions.BearerScheme);
        builder.Services.AddSingleton<IPortalSessionClaimsReader>(claimsReader ?? new StubClaimsReader());
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]>>(new StubListHandler());
        builder.Services.AddSingleton<ITeacherScopeProvider>(new StubScopeProvider());
        builder.Services.AddSingleton<ICurrentUser, StubCurrentUser>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    private static HttpRequestMessage SessionRequest(string sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/assignments");
        request.Headers.Add(PortalSessionAuthenticationHandler.SessionHeaderName, sessionId);
        return request;
    }

    [TestMethod]
    public async Task ReaderPolicy_NamesTheGatewayScheme_AndNoLongerBearer()
    {
        await using var app = await StartHostAsync();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e => (e.RoutePattern.RawText ?? string.Empty).Trim('/') == "assignments"
                && e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);
        var reader = endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>()
            .Where(p => p.Requirements.OfType<RolesAuthorizationRequirement>().Any())
            .ToList();

        reader.Should().ContainSingle("the covered list route carries exactly one role-bearing policy");
        reader[0].AuthenticationSchemes.Should().Equal([PortalSessionAuthenticationHandler.GatewaySchemeName],
            "the reader policy's single scheme is the portal-session gateway (D19) — registered in " +
            "every flag state, so the challenge can never 500");
        reader[0].AuthenticationSchemes.Should().NotContain(AuthTenancyExtensions.BearerScheme);
    }

    [TestMethod]
    public async Task ATeacherPresentingALiveSession_AuthorizesWithTheD9ClaimSet()
    {
        var claimsReader = new StubClaimsReader();
        await using var app = await StartHostAsync(claimsReader);
        using var client = CreateClient(app);

        var response = await client.SendAsync(SessionRequest("a-live-session-id"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the portal-session principal carries the teacher role, so RequireRole succeeds");
        claimsReader.LastSessionId.Should().Be("a-live-session-id",
            "the handler resolved the request's session id through the claims port");
    }

    [TestMethod]
    public async Task AnUnknownSession_IsABare401()
    {
        var claimsReader = new StubClaimsReader { NextClaims = null };
        await using var app = await StartHostAsync(claimsReader);
        using var client = CreateClient(app);

        var response = await client.SendAsync(SessionRequest("not-a-live-session"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull("the gateway's challenge is a bare 401, never a redirect");
    }

    [TestMethod]
    public async Task ABearerCaller_StillAuthorizes_ThroughThePinnedFallback()
    {
        // AC2: the fallback pin restores today's exact authentication union for bearer callers —
        // the gateway routes them to Bearer (never the host default's OIDC cookie).
        await using var app = await StartHostAsync();
        using var client = CreateClient(app);
        client.DefaultRequestHeaders.Add("x-test-roles", RealmRoleNames.Teacher);

        var response = await client.GetAsync("/assignments");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an existing bearer caller authorizes exactly as before the adoption");
    }

    [TestMethod]
    public async Task AnAnonymousCaller_WithNoSessionHeader_Is401()
    {
        await using var app = await StartHostAsync();
        using var client = CreateClient(app);

        var response = await client.GetAsync("/assignments");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── the D2 wire contract, over the REAL reader ──────────────────────────────────────────

    private const string WireSessionId = "9f0c1a2b-4d3e-4f5a-8b9c-0d1e2f3a4b5c";
    private const string WireTenantId = "11111111-1111-1111-1111-111111111111";
    private const string WireTenantName = "Wire School";
    private const string WireTenantType = "School";
    private const string WireTeacherId = "22222222-2222-2222-2222-222222222222";

    /// <summary>The D2 response body, field for field: minimal APIs serialize the auth service's
    /// <c>SessionClaimsResponse</c> through the JSON Web defaults, so every key is camelCase. It is
    /// rebuilt here with those same settings over the same field names — this test project cannot
    /// reference the auth context (the shared kernel is the only cross-context allowance), so the
    /// pin is the contract, not a copy of the record.</summary>
    private sealed record SessionClaimsWireBody(
        string TenantId,
        string TenantName,
        string TenantType,
        string TeacherId,
        IReadOnlyList<string> Roles);

    /// <summary>The serialized D2 body, exactly as the auth route emits it.</summary>
    private static string SerializeSessionClaimsBody() =>
        JsonSerializer.Serialize(
            new SessionClaimsWireBody(
                WireTenantId, WireTenantName, WireTenantType, WireTeacherId, [RealmRoleNames.Teacher, "user-admin"]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>The REAL reader over a scripted handler. A request whose path is not
    /// <c>/auth/session/{id}/claims</c> matches no scripted response and throws, so the route the
    /// reader builds is covered by the same match.</summary>
    private static PortalSessionClaimsReader CreateReader(string body)
    {
        var handler = new MockHttpMessageHandler();
        handler.When("*/auth/session/*/claims").Respond("application/json", body);
        return new PortalSessionClaimsReader(
            new HttpClient(handler) { BaseAddress = new Uri("http://auth") },
            NullLogger<PortalSessionClaimsReader>.Instance);
    }

    [TestMethod]
    public async Task TheReader_ParsesTheSerializedClaimsBody_IntoTheD9ClaimSet()
    {
        var body = SerializeSessionClaimsBody();

        // The body's spelling is asserted, never assumed: it is the half of the wire contract the
        // reader must match, and a hand-written literal would let the two halves drift apart
        // silently (exactly the fail-closed-but-feature-dead break this pins).
        body.Should().Contain("\"tenantId\"").And.Contain("\"tenantName\"").And.Contain("\"tenantType\"")
            .And.Contain("\"teacherId\"").And.Contain("\"roles\"");
        body.Should().NotContain("tenant_id").And.NotContain("tenant_name").And.NotContain("tenant_type")
            .And.NotContain("teacher_id");

        var claims = await CreateReader(body).ReadClaimsAsync(WireSessionId);

        claims.Should().NotBeNull("the D2 body carries the pinned claim contract");
        claims!.TenantId.Should().Be(WireTenantId);
        claims.TenantName.Should().Be(WireTenantName);
        claims.TenantType.Should().Be(WireTenantType);
        claims.TeacherId.Should().Be(WireTeacherId);
        claims.Roles.Should().Equal(RealmRoleNames.Teacher, "user-admin");
    }

    [TestMethod]
    public async Task TheReader_RefusesTheClaimNameSpelling_FailClosed()
    {
        // The discrimination's other direction: the D9 claim names are NOT the wire spelling, so a
        // body spelled that way is not the D2 contract and must fail the authentication closed
        // rather than resolve a half-read claim set.
        var reader = CreateReader(
            $$"""
            {"tenant_id":"{{WireTenantId}}","tenant_name":"{{WireTenantName}}","tenant_type":"{{WireTenantType}}","teacher_id":"{{WireTeacherId}}","roles":["{{RealmRoleNames.Teacher}}"]}
            """);

        (await reader.ReadClaimsAsync(WireSessionId)).Should().BeNull();
    }
}
