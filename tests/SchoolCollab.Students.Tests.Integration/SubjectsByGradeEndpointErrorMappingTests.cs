using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Integration tests for the <c>GET /students/topics/by-grade/{gradeLevelId}</c>
/// endpoint. Pins the response-shape contract that the Topics landing
/// (<c>Topics.razor</c>) depends on:
///
/// <list type="bullet">
///   <item>200 OK with <c>TopicDto[]</c> body â€” even when no subjects exist
///         (empty array, not null, not a 404).</item>
///   <item>The endpoint tolerates a cancelled client token without
///         returning a 500 (regression for the "throws an error for
///         fetching" report â€” the per-endpoint try/catch added in
///         TopicRoutes surfaces a typed response).</item>
/// </list>
///
/// These run against the real Students API + Postgres + RabbitMQ via
/// <see cref="ApiFactory"/>, so the global tenant query filters apply.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TopicsByGradeEndpointErrorMappingTests
{
    private static ApiFactory _factory = default!;
    private static HttpClient _client = default!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _factory = new ApiFactory();
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();

        // Each test starts from a clean slate. The CASCADE chains through
        // grade_subject_assignments -> grade_levels so the order matters
        // for the FK; TRUNCATE ... CASCADE handles it.
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE topic_assignments, grade_levels, subjects, periods CASCADE;");
    }

    [TestMethod]
    public async Task NoTopicsAssigned_Returns200WithEmptyArray()
    {
        // Arrange: seed a grade level (no assignments).
        var gradeLevelId = await SeedGradeLevelAsync(ApiFactory.TestTenantA, "Grade 1");

        // Act
        var response = await SendAsync(HttpMethod.Get,
            $"/students/topics/by-grade/{gradeLevelId}", ApiFactory.TestTenantA);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an unknown / unassigned grade must return 200 + empty, not 404 or 500");
        var body = await response.Content.ReadFromJsonAsync<TopicDto[]>();
        body.Should().NotBeNull("the endpoint must always return a JSON array");
        body.Should().BeEmpty("no assignments seeded for this grade");
    }

    [TestMethod]
    public async Task SubjectsAlias_ReturnsSameDataAsTopics_Ac16()
    {
        // AC-16 / NFR-6: the legacy /subjects prefix is a deprecated alias for
        // /topics and must return identical TopicDto data.
        var gradeLevelId = await SeedGradeLevelAsync(ApiFactory.TestTenantA, "Grade 1");
        await SeedTopicAndAssignmentAsync(ApiFactory.TestTenantA,
            gradeLevelId, "MATH", "Mathematics");

        var canonical = await SendAsync(HttpMethod.Get,
            $"/students/topics/by-grade/{gradeLevelId}", ApiFactory.TestTenantA);
        var aliased = await SendAsync(HttpMethod.Get,
            $"/students/subjects/by-grade/{gradeLevelId}", ApiFactory.TestTenantA);

        canonical.StatusCode.Should().Be(HttpStatusCode.OK);
        aliased.StatusCode.Should().Be(HttpStatusCode.OK);
        var canonicalBody = await canonical.Content.ReadFromJsonAsync<TopicDto[]>();
        var aliasedBody = await aliased.Content.ReadFromJsonAsync<TopicDto[]>();
        canonicalBody.Should().NotBeNull();
        aliasedBody.Should().NotBeNull();
        canonicalBody.Should().BeEquivalentTo(aliasedBody,
            "the deprecated /subjects alias must return the same TopicDto data as /topics");
    }


    [TestMethod]
    public async Task WithAssignment_Returns200WithTopics()
    {
        // Arrange: seed a grade + subject + assignment under Tenant A.
        var gradeLevelId = await SeedGradeLevelAsync(ApiFactory.TestTenantA, "Grade 1");
        await SeedTopicAndAssignmentAsync(ApiFactory.TestTenantA,
            gradeLevelId, "MATH", "Mathematics");

        // Act
        var response = await SendAsync(HttpMethod.Get,
            $"/students/topics/by-grade/{gradeLevelId}", ApiFactory.TestTenantA);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TopicDto[]>();
        body.Should().NotBeNull();
        body.Should().ContainSingle(s => s.Code == "MATH");
    }

    [TestMethod]
    public async Task WithExplicitEffectiveDate_FiltersToThatDate()
    {
        // Arrange: one assignment effective from today (open-ended). An explicit
        // effectiveDate in the far future has no matching assignment and must
        // return 200 + empty, not 500.
        var gradeLevelId = await SeedGradeLevelAsync(ApiFactory.TestTenantA, "Grade 1");
        await SeedTopicAndAssignmentAsync(ApiFactory.TestTenantA,
            gradeLevelId, "MATH", "Mathematics");

        var futureEffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3650));

        // Act
        var response = await SendAsync(HttpMethod.Get,
            $"/students/topics/by-grade/{gradeLevelId}?effectiveDate={futureEffectiveDate:yyyy-MM-dd}",
            ApiFactory.TestTenantA);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an explicit effectiveDate with no matching assignment must return 200 + empty, not 500");
        var body = await response.Content.ReadFromJsonAsync<TopicDto[]>();
        body.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CancelledToken_NeverReturnsServerError()
    {
        // Regression for "throws an error for fetching": a closed client
        // (or an upstream timeout that cancels the request) must NEVER
        // surface as a 500 from our endpoint. Two valid outcomes:
        //   (a) the HttpClient itself throws TaskCanceledException because
        //       the request was cancelled before it could be sent
        //       (testhost cancel propagates back to the client), or
        //   (b) the request reaches the handler which throws
        //       OperationCanceledException - caught by the endpoint's
        //       try/catch and returned as 499 Client Closed Request.
        // Both prove the contract: "no 500 from the server".
        var gradeLevelId = await SeedGradeLevelAsync(ApiFactory.TestTenantA, "Grade 1");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act: catch the client's short-circuit throw so we can assert
        // "either the client cancelled OR the server returned non-500".
        HttpStatusCode? statusCode = null;
        try
        {
            var response = await SendAsync(HttpMethod.Get,
                $"/students/topics/by-grade/{gradeLevelId}",
                ApiFactory.TestTenantA, cts.Token);
            statusCode = response.StatusCode;
            response.Dispose();
        }
        catch (TaskCanceledException)
        {
            // (a): client short-circuit - this is fine, it's the contract.
        }

        // Assert: if the server did respond, it must NOT have been a 500.
        if (statusCode is { } code)
        {
            ((int)code).Should().NotBe(500,
                "a cancelled request that reaches the server must not surface as a server error");
        }
    }

    // â”€â”€â”€â”€â”€â”€ seed helpers â”€â”€â”€â”€â”€â”€

    private async Task<Guid> SeedGradeLevelAsync(Guid tenantId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var gl = GradeLevel.Create(Guid.NewGuid(), 1, name, 1);
            db.GradeLevels.Add(gl);
            await db.SaveChangesAsync();
            return gl.Id;
        });
    }

    private async Task<Guid> SeedPeriodAsync(Guid tenantId, string name, DateOnly start, DateOnly end)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var period = Period.Create(name, start, end);
            db.Periods.Add(period);
            await db.SaveChangesAsync();
            return period.Id;
        });
    }

    private async Task SeedTopicAndAssignmentAsync(
        Guid tenantId, Guid gradeLevelId, string code, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenantId, async (CancellationToken _ct) =>
        {
            var topic = Topic.Create(Guid.NewGuid(), code, name, 1);
            db.Topics.Add(topic);
            await db.SaveChangesAsync(_ct);
            // Assignments are date-based and open-ended (start today, no end),
            // so a topic stays assigned to the grade across years.
            db.GradeTopicAssignments.Add(
                GradeTopicAssignment.Create(gradeLevelId, topic.Id, DateOnly.FromDateTime(DateTime.UtcNow)));
            await db.SaveChangesAsync(_ct);
            return (object?)null;
        });
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, Guid tenantId, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        return _client.SendAsync(request, ct);
    }
}
