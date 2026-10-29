using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Api;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Api.Tests.Unit;

/// <summary>
/// Issue #444 (layer L2b) — wire tests for <code>GET /students/{id}/assignment-stats</code>: the Students
/// endpoint the view calls, whose own hop failure is the round's Open decision A ("5xx problem, logged,
/// never a 200 carrying zeros"). Follows the <c>StudentTargetRoutesTests.BuildApp</c> precedent: a bare
/// <c>WebApplication</c> on <c>UseTestServer</c> with only <c>MapStudentEndpoints</c> mapped — no Postgres,
/// no RabbitMQ, no <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
[TestClass]
public class StudentStatsRoutesTests
{
    private static readonly Guid StudentId = Guid.Parse("a0000000-0000-0000-0000-0000000000ff");

    private static readonly StudentAssignmentStats Counts = new(
        Assigned: 24, SentOut: 21, Submitted: 18, Marked: 15, ReviewRequired: 12, Reviewed: 10);

    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.DisableOIDCAuth;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) =>
            Task.FromResult(IsEnabled(featureKey));
    }

    /// <summary>The port's own contract: it throws when the Assignments API cannot be reached, and the
    /// endpoint decides what that means for the wire.</summary>
    private sealed class StubReader(Func<Guid, StudentAssignmentStats> answer) : IStudentAssignmentStatsReader
    {
        public List<Guid> RequestedStudentIds { get; } = [];

        public Task<StudentAssignmentStats> GetAsync(Guid studentId, CancellationToken cancellationToken = default)
        {
            RequestedStudentIds.Add(studentId);
            return Task.FromResult(answer(studentId));
        }
    }

    private static WebApplication BuildApp(out HttpClient client, IStudentAssignmentStatsReader reader)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(reader);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());

        var app = builder.Build();
        app.MapStudentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        app.StartAsync().GetAwaiter().GetResult();

        client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();
        return app;
    }

    [TestMethod]
    public async Task AssignmentStats_ServesTheDocumentedPathWithTheSixCounts()
    {
        var reader = new StubReader(_ => Counts);
        await using var app = BuildApp(out var client, reader);

        var response = await client.GetAsync($"/students/{StudentId}/assignment-stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var json = await response.Content.ReadAsStringAsync();
        var stats = JsonSerializer.Deserialize<StudentAssignmentStats>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        stats.Should().Be(Counts, "the endpoint forwards the port's answer verbatim");
        reader.RequestedStudentIds.Should().ContainSingle()
            .Which.Should().Be(StudentId, "the route binds the student id from the path");
    }

    [TestMethod]
    public async Task AssignmentStats_HopFailure_ReturnsAProblemInsteadOfZeroCounts()
    {
        // Open decision A: a 200 carrying available:false (or six zeros) would hide a cross-context
        // breakage from telemetry — the one place it should be loud.
        var reader = new StubReader(_ => throw new HttpRequestException("assignments-api is down"));
        await using var app = BuildApp(out var client, reader);

        var response = await client.GetAsync($"/students/{StudentId}/assignment-stats");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway,
            "a failed hop is a 5xx problem, never a 200 that claims zeros");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Assignment stats are unavailable");
    }
}
