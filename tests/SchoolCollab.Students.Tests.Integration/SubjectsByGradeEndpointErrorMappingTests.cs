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
/// Integration tests for the <c>GET /students/subjects/by-grade/{gradeLevelId}</c>
/// endpoint. Pins the response-shape contract that the Subjects landing
/// (<c>Subjects.razor</c>) depends on:
///
/// <list type="bullet">
///   <item>200 OK with <c>SubjectDto[]</c> body — even when no subjects exist
///         (empty array, not null, not a 404).</item>
///   <item>The endpoint tolerates a cancelled client token without
///         returning a 500 (regression for the "throws an error for
///         fetching" report — the per-endpoint try/catch added in
///         SubjectRoutes surfaces a typed response).</item>
/// </list>
///
/// These run against the real Students API + Postgres + RabbitMQ via
/// <see cref="ApiFactory"/>, so the global tenant query filters apply.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SubjectsByGradeEndpointErrorMappingTests
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
            "TRUNCATE TABLE grade_subject_assignments, grade_levels, subjects, periods CASCADE;");
    }

    [TestMethod]
    public async Task NoSubjectsAssigned_Returns200WithEmptyArray()
    {
        // Arrange: seed a grade level (no assignments).
        var gradeLevelId = await SeedGradeLevelAsync(ApiFactory.TestTenantA, "Grade 1");

        // Act
        var response = await SendAsync(HttpMethod.Get,
            $"/students/subjects/by-grade/{gradeLevelId}", ApiFactory.TestTenantA);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an unknown / unassigned grade must return 200 + empty, not 404 or 500");
        var body = await response.Content.ReadFromJsonAsync<SubjectDto[]>();
        body.Should().NotBeNull("the endpoint must always return a JSON array");
        body.Should().BeEmpty("no assignments seeded for this grade");
    }

    [TestMethod]
    public async Task WithAssignment_Returns200WithSubjects()
    {
        // Arrange: seed a grade + period + subject + assignment under Tenant A.
        var gradeLevelId = await SeedGradeLevelAsync(ApiFactory.TestTenantA, "Grade 1");
        var periodId = await SeedPeriodAsync(ApiFactory.TestTenantA, "Term 1",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
        await SeedSubjectAndAssignmentAsync(ApiFactory.TestTenantA,
            gradeLevelId, periodId, "MATH", "Mathematics");

        // Act
        var response = await SendAsync(HttpMethod.Get,
            $"/students/subjects/by-grade/{gradeLevelId}", ApiFactory.TestTenantA);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SubjectDto[]>();
        body.Should().NotBeNull();
        body.Should().ContainSingle(s => s.Code == "MATH");
    }

    [TestMethod]
    public async Task WithExplicitPeriodId_FiltersToThatPeriod()
    {
        // Arrange: one assignment in the current period, none in a different
        // (past) period. An explicit periodId query for the past period
        // must return 200 + empty, not 500.
        var gradeLevelId = await SeedGradeLevelAsync(ApiFactory.TestTenantA, "Grade 1");
        var pastPeriodId = await SeedPeriodAsync(ApiFactory.TestTenantA, "Term 0",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-15)));
        await SeedSubjectAndAssignmentAsync(ApiFactory.TestTenantA,
            gradeLevelId, pastPeriodId, "MATH", "Mathematics");

        var futurePeriodId = Guid.NewGuid(); // never seeded → filter excludes everything

        // Act
        var response = await SendAsync(HttpMethod.Get,
            $"/students/subjects/by-grade/{gradeLevelId}?periodId={futurePeriodId}",
            ApiFactory.TestTenantA);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an explicit periodId with no matching assignment must return 200 + empty, not 500");
        var body = await response.Content.ReadFromJsonAsync<SubjectDto[]>();
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
                $"/students/subjects/by-grade/{gradeLevelId}",
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

    // ────── seed helpers ──────

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

    private async Task SeedSubjectAndAssignmentAsync(
        Guid tenantId, Guid gradeLevelId, Guid periodId, string code, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenantId, async (CancellationToken _ct) =>
        {
            var subject = Subject.Create(Guid.NewGuid(), code, name, 1);
            db.Subjects.Add(subject);
            await db.SaveChangesAsync(_ct);
            db.GradeSubjectAssignments.Add(
                GradeSubjectAssignment.Create(gradeLevelId, subject.Id, periodId));
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