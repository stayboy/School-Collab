using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Period deactivation endpoint (documents/specs/period-edit-parity-deactivate.md
/// FR-X7/X8/X10, AC-E8/E9). POST /students/periods/{id}/deactivate → 204; repeat →
/// 422 (already Deactivated); 404 unknown/other-tenant; 422 non-Active; and a
/// Deactivated period's date range no longer blocks a new period (FR-X3).
/// </summary>
[TestClass]
[DoNotParallelize]
public class PeriodDeactivateEndpointTests
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

    private static CreatePeriod NewYear(string name, int year) =>
        new(name, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), AcademicYearDivision.None);

    private async Task<Guid> CreateAndActivateAsync(Guid tenantId, string name, int year)
    {
        var create = await PostAsync("/students/periods", tenantId, NewYear(name, year));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<CreatePeriodIdResponse>())!.Id;
        var activate = await PostAsync("/students/periods/{id}/activate".Replace("{id}", id.ToString()), tenantId, null);
        activate.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return id;
    }

    // AC-E8: deactivating an Active period returns 204.
    [TestMethod]
    public async Task Deactivate_ActivePeriod_Returns204()
    {
        var id = await CreateAndActivateAsync(ApiFactory.TestTenantA, "AY2026", 2026);

        var response = await PostAsync($"/students/periods/{id}/deactivate", ApiFactory.TestTenantA, null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // AC-E8: deactivating an already-Deactivated period is a 422 (no idempotent no-op).
    [TestMethod]
    public async Task Deactivate_AlreadyDeactivated_Returns422()
    {
        var id = await CreateAndActivateAsync(ApiFactory.TestTenantA, "AY2026", 2026);
        await PostAsync($"/students/periods/{id}/deactivate", ApiFactory.TestTenantA, null);

        var response = await PostAsync($"/students/periods/{id}/deactivate", ApiFactory.TestTenantA, null);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // AC-E8: an unknown id resolves to 404.
    [TestMethod]
    public async Task Deactivate_UnknownId_Returns404()
    {
        var response = await PostAsync($"/students/periods/{Guid.NewGuid()}/deactivate", ApiFactory.TestTenantA, null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // AC-E9: another tenant's Active period id resolves to 404 and no row changes.
    [TestMethod]
    public async Task Deactivate_OtherTenantsPeriod_Returns404()
    {
        var id = await CreateAndActivateAsync(ApiFactory.TestTenantA, "AY2026", 2026);

        var response = await PostAsync($"/students/periods/{id}/deactivate", ApiFactory.TestTenantB, null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        var status = await accessor.RunWithExplicitTenantAsync(
            ApiFactory.TestTenantA,
            ct => db.Periods.Where(p => p.Id == id).Select(p => p.Status).SingleAsync(ct));
        status.Should().Be(PeriodStatus.Active,
            "the other tenant's deactivate must not change the row");
    }

    // AC-E4: a non-Active (Draft) period is rejected with 422.
    [TestMethod]
    public async Task Deactivate_DraftPeriod_Returns422()
    {
        var create = await PostAsync("/students/periods", ApiFactory.TestTenantA, NewYear("AY2026", 2026));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<CreatePeriodIdResponse>())!.Id;

        var response = await PostAsync($"/students/periods/{id}/deactivate", ApiFactory.TestTenantA, null);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // FR-X3 / AC-E6: after deactivating a blocker, a new period in the same range succeeds.
    [TestMethod]
    public async Task Deactivate_FreesOverlap_ForNewPeriod()
    {
        var id = await CreateAndActivateAsync(ApiFactory.TestTenantA, "AY2026", 2026);
        await PostAsync($"/students/periods/{id}/deactivate", ApiFactory.TestTenantA, null);

        var create = await PostAsync("/students/periods", ApiFactory.TestTenantA, NewYear("AY2026 corrected", 2026));
        create.StatusCode.Should().Be(HttpStatusCode.Created,
            "a Deactivated period no longer blocks a corrected new period (FR-X3)");
    }

    // ---- request helpers: tenant stamped per-request via the x-tenant-id header. ----

    private Task<HttpResponseMessage> PostAsync(string path, Guid tenantId, object? body) =>
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

    private sealed record CreatePeriodIdResponse(Guid Id);
}
