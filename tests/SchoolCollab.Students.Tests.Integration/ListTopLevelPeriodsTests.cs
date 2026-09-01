using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Integration tests for <c>GET /students/periods/top-level</c>
/// (<see cref="SchoolCollab.Students.Core.CQRS.Periods.Queries.ListTopLevelPeriods.ListTopLevelPeriodsHandler"/>):
/// the Periods landing grid endpoint that returns academic years only, with
/// server-computed sub-period counts.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ListTopLevelPeriodsTests
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
        var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();

        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE periods CASCADE;");
        await cache.RemoveByTagAsync("students");
    }

    [TestMethod]
    public async Task TopLevel_ExcludesSubPeriods_AndReportsCounts()
    {
        // A Terms year with two Draft sub-periods created atomically (FR-C1).
        var create = await PostAsync("/students/periods", ApiFactory.TestTenantA,
            new CreatePeriod(
                "Year 2026",
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31),
                AcademicYearDivision.Terms,
                SubPeriods:
                [
                    new SubPeriodDefinition("Term 1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)),
                    new SubPeriodDefinition("Term 2", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)),
                ]));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var rows = (await (await GetAsync("/students/periods/top-level", ApiFactory.TestTenantA)).Content
            .ReadFromJsonAsync<PeriodLandingDto[]>())!;

        rows.Should().ContainSingle("the tenant has exactly one top-level academic year");
        var year = rows.Single();
        year.Name.Should().Be("Year 2026");
        year.Division.Should().Be("Terms");
        year.SubPeriodCount.Should().Be(2,
            "the server must compute the sub-period count so the UI needs no sub-period rows");
        year.DraftSubPeriodCount.Should().Be(2,
            "freshly created sub-periods start as Draft (drives the FR-G6 Activate guard)");

        // The sub-period rows themselves must NOT appear as landing rows.
        rows.Should().NotContain(r => r.Name == "Term 1" || r.Name == "Term 2");
    }

    [TestMethod]
    public async Task TopLevel_TenantScoped_CacheDoesNotLeakBetweenTenants()
    {
        var aCreate = await PostAsync("/students/periods", ApiFactory.TestTenantA,
            new CreatePeriod("Year A", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.None));
        aCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        // Tenant B (cold cache) must see an empty list, not A's rows.
        var bRows = (await (await GetAsync("/students/periods/top-level", ApiFactory.TestTenantB)).Content
            .ReadFromJsonAsync<PeriodLandingDto[]>())!;
        bRows.Should().BeEmpty("Tenant B has no periods of its own");

        // Tenant A's cache key is tenant-scoped, so A still sees its own year.
        var aRows = (await (await GetAsync("/students/periods/top-level", ApiFactory.TestTenantA)).Content
            .ReadFromJsonAsync<PeriodLandingDto[]>())!;
        aRows.Should().ContainSingle(p => p.Name == "Year A");
    }

    // ---- request helpers: tenant is stamped per-request via the x-tenant-id header
    //      that TestAuthHandler honors (see SchoolCollab.Core.Auth.TestAuthHandler). ----

    private Task<HttpResponseMessage> GetAsync(string path, Guid tenantId) =>
        SendAsync(HttpMethod.Get, path, tenantId);

    private Task<HttpResponseMessage> PostAsync(string path, Guid tenantId, object body) =>
        SendAsync(HttpMethod.Post, path, tenantId, body);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, Guid tenantId, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }
}