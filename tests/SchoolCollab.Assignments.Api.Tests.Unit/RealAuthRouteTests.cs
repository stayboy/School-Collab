using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// ar-24 AC3 — real-auth (flag=false) 401-with-no-token on the assigned endpoint groups,
/// exercised on the CONTAINER-FREE TestServer harness (the SignOffRoutesTests precedent:
/// a minimal in-process host that maps ONLY the endpoint groups, so no Postgres / RabbitMQ
/// / Settings-api is needed). The authorization middleware rejects BEFORE any handler /
/// service resolution, so a no-token request yields 401 without a DB or external authority
/// (AddJwtBearer validates lazily — no token, no authority contact).
/// </summary>
[TestClass]
public class RealAuthRouteTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static async Task<WebApplication> StartHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Real-auth mode: the canonical flag key, plus a dummy (https) metadata authority.
        // AddJwtBearer is lazy — with no token sent, the 401 fires before any authority contact.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureFlags:FEATURE:DisableOIDCAuth"] = "false",
            ["Auth:Keycloak:Authority"] = "https://keycloak.test/realms/school-collab",
            ["Auth:Keycloak:ClientId"] = "school-collab-client",
            ["Auth:Keycloak:ClientSecret"] = "dev-credentials",
        });

        builder.Services.AddAuthAndTenancy(builder.Configuration);

        // D19/D7 (portal-session-adoption): the reader policy names the GATEWAY scheme, and a
        // policy-named scheme without a handler is a 500 — never a 401 — so this host registers
        // the gateway exactly as Program.cs does, over a stubbed claims port (the moved handler's
        // required ctor dependency; an unregistered dependency is a DI failure, not a 401).
        builder.Services.AddAuthentication().AddPortalSessionAuthentication(AuthTenancyExtensions.BearerScheme);
        builder.Services.AddSingleton<IPortalSessionClaimsReader>(new StubPortalSessionClaimsReader());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    /// <summary>D19/D7: the stub claims port the moved portal-session handler authenticates through
    /// on this host — a no-token request never presents a session id, so the gateway resolves the
    /// pinned Bearer fallback and the 401 stays the bearer scheme's.</summary>
    private sealed class StubPortalSessionClaimsReader : IPortalSessionClaimsReader
    {
        public ValueTask<PortalClaims?> ReadClaimsAsync(string sessionId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<PortalClaims?>(null);
    }

    [TestMethod]
    public async Task NoToken_AssignmentsGroup_Is401()
    {
        await using var app = await StartHostAsync();
        using var client = CreateClient(app);
        (await client.GetAsync("/assignments")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task NoToken_WardList_Is401()
    {
        await using var app = await StartHostAsync();
        using var client = CreateClient(app);
        (await client.GetAsync($"/students/{StudentId}/assignments")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task NoToken_ReviewQueue_Is401()
    {
        await using var app = await StartHostAsync();
        using var client = CreateClient(app);
        (await client.GetAsync($"/assignments/{AssignmentId}/submissions/review-queue")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task NoToken_Approve_Is401()
    {
        await using var app = await StartHostAsync();
        using var client = CreateClient(app);
        (await client.PostAsync($"/assignments/{AssignmentId}/approve", new StringContent("""{"approverId":"00000000-0000-0000-0000-000000000001"}""", System.Text.Encoding.UTF8, "application/json")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
