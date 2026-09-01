using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.Tenancy;

namespace SchoolCollab.Students.Tests.Integration;

[TestClass]
[DoNotParallelize]
public class PeriodWizardOpenTermGateTests
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

        // Each test starts from a clean DB and a flushed "students" cache tag so the
        // per-tenant list cache cannot leak stale state across tests.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE periods CASCADE;");
        await cache.RemoveByTagAsync("students");
    }

    // The GradeLevel wizard derives its "open a term" gate from this list query:
    // empty  => show the Open-Term form; non-empty => show the confirmation card.
    [TestMethod]
    public async Task WizardOpenTermGate_NoExistingPeriod_ListIsEmpty()
    {
        var response = await GetAsync("/students/periods", ApiFactory.TestTenantA);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var periods = (await response.Content.ReadFromJsonAsync<PeriodDto[]>())!;
        periods.Should().BeEmpty(
            "with no period created for the tenant, the wizard gate must offer to open a new term");
    }

    [TestMethod]
    public async Task WizardOpenTermGate_OpenTerm_CreatesPeriodAndListReflectsIt()
    {
        // Gate starts empty...
        (await (await GetAsync("/students/periods", ApiFactory.TestTenantA)).Content
            .ReadFromJsonAsync<PeriodDto[]>())!.Should().BeEmpty();

        // ...then the user "opens a term" -> CreatePeriodHandler.
        var create = await PostAsync("/students/periods", ApiFactory.TestTenantA,
            new CreatePeriod("Term 2025", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), AcademicYearDivision.None));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // Gate now surfaces the existing term (confirmation card).
        var periods = (await (await GetAsync("/students/periods", ApiFactory.TestTenantA)).Content
            .ReadFromJsonAsync<PeriodDto[]>())!;
        periods.Should().ContainSingle(p =>
            p.Name == "Term 2025" && p.StartDate == new DateOnly(2025, 1, 1));
    }

    // Regression for the originally-reported bug: reads showed an empty list (stale
    // global cache) while CreatePeriodHandler then threw PeriodOverlapException
    // because the table was not actually empty. The list cache key is now
    // per-tenant, so one tenant's cold-cache empty list can never mask another
    // tenant's existing period.
    [TestMethod]
    public async Task ListPeriods_TenantScoped_CacheDoesNotLeakBetweenTenants()
    {
        // Tenant A already has a period.
        var aCreate = await PostAsync("/students/periods", ApiFactory.TestTenantA,
            new CreatePeriod("Term A", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.None));
        aCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        // Tenant B queries the same logical list first (cold cache for B).
        var bFirst = (await (await GetAsync("/students/periods", ApiFactory.TestTenantB)).Content
            .ReadFromJsonAsync<PeriodDto[]>())!;
        bFirst.Should().BeEmpty("Tenant B has no periods of its own");

        // Tenant A queries the same logical list afterwards. With the fix the key is
        // per-tenant, so A sees its own period (the bug returned B's stale empty).
        var aList = (await (await GetAsync("/students/periods", ApiFactory.TestTenantA)).Content
            .ReadFromJsonAsync<PeriodDto[]>())!;
        aList.Should().ContainSingle(p => p.Name == "Term A",
            "A's list must reflect A's own period, not B's cached empty list");
    }

    // Proves the list is actually served from HybridCache (not just the DB) and that
    // CreatePeriodHandler invalidates it via the "students" tag.
    [TestMethod]
    public async Task ListPeriods_IsCachedAndInvalidatedOnCreate()
    {
        // Plant a stale value under Tenant A's tenant-scoped cache key.
        using (var plantScope = _factory.Services.CreateScope())
        {
            var cache = plantScope.ServiceProvider.GetRequiredService<HybridCache>();
            await cache.RemoveByTagAsync("students");
            var stale = new[]
            {
                new PeriodDto(
                    Guid.NewGuid(), "STALE FAKE", new DateOnly(2000, 1, 1), new DateOnly(2000, 12, 31),
                    "Draft", null, null, "None", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            };
            // Tagged "students" so the handler's RemoveByTagAsync("students") clears it.
            await cache.SetAsync(
                "periods:list:" + ApiFactory.TestTenantA,
                stale,
                new HybridCacheEntryOptions(),
                new[] { "students" });
        }

        // ListPeriodsHandler must read the planted value straight from the cache.
        var cached = (await (await GetAsync("/students/periods", ApiFactory.TestTenantA)).Content
            .ReadFromJsonAsync<PeriodDto[]>())!;
        cached.Should().ContainSingle(p => p.Name == "STALE FAKE",
            "ListPeriodsHandler must serve the tenant-scoped cache entry");

        // Opening a real term must invalidate the cached list (regression guard).
        var create = await PostAsync("/students/periods", ApiFactory.TestTenantA,
            new CreatePeriod("Real Term", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), AcademicYearDivision.None));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = (await (await GetAsync("/students/periods", ApiFactory.TestTenantA)).Content
            .ReadFromJsonAsync<PeriodDto[]>())!;
        after.Should().NotContain(p => p.Name == "STALE FAKE",
            "CreatePeriodHandler must invalidate the cached list");
        after.Should().ContainSingle(p => p.Name == "Real Term");
    }

    // The "at most one active/current period per tenant" invariant is enforced by
    // CreatePeriodHandler against GetOverlappingPeriodsAsync, which is tenant-scoped
    // (global query filter on Period). Overlap is rejected within a tenant but a
    // different tenant is free to reuse the same dates.
    [TestMethod]
    public async Task CreatePeriod_TenantScoped_OverlapRejectedWithinTenantNotAcross()
    {
        // Tenant A opens Term A covering all of 2025.
        var a1 = await PostAsync("/students/periods", ApiFactory.TestTenantA,
            new CreatePeriod("Term A", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), AcademicYearDivision.None));
        a1.StatusCode.Should().Be(HttpStatusCode.Created);

        // A second, overlapping term for the SAME tenant is rejected with 422 (the
        // route maps PeriodOverlapException to UnprocessableEntity).
        var sameTenantOverlap = await PostAsync("/students/periods", ApiFactory.TestTenantA,
            new CreatePeriod("Term A2", new DateOnly(2025, 6, 1), new DateOnly(2025, 7, 31), AcademicYearDivision.None));
        sameTenantOverlap.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "overlapping a period within the same tenant must be rejected (422)");

        // The SAME dates for a DIFFERENT tenant are accepted (per-tenant overlap check).
        var bSame = await PostAsync("/students/periods", ApiFactory.TestTenantB,
            new CreatePeriod("Term B", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), AcademicYearDivision.None));
        bSame.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ActivePeriodProvider backs ambient period resolution (and the wizard's
    // active/current term detection elsewhere). Its cached queries run inside the
    // HybridCache factory where the tenant context is lost, so the period query is
    // scoped explicitly by TenantId. This proves the cached current-period lookup
    // is tenant-correct (the previously-broken path returned null for the real
    // tenant because the global filter resolved to Guid.Empty).
    [TestMethod]
    public async Task ActivePeriodProvider_TenantScoped_CurrentPeriod()
    {
        // Seed a period covering "today" (2026-07-11) for Tenant A only, then activate
        // it so the active-period provider resolves it.
        var create = await PostAsync("/students/periods", ApiFactory.TestTenantA,
            new CreatePeriod("Current A", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.None));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<CreatePeriodIdResponse>())!.Id;
        var activate = await PostAsync($"/students/periods/{id}/activate", ApiFactory.TestTenantA, null);
        activate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var periodProvider = scope.ServiceProvider.GetRequiredService<IActivePeriodProvider>();

        var currentA = await accessor.RunWithExplicitTenantAsync(
            ApiFactory.TestTenantA,
            ct => periodProvider.GetCurrentPeriodAsync(ct));
        currentA.Should().NotBeNull("Tenant A has a period covering today");
        currentA!.Name.Should().Be("Current A");

        var currentB = await accessor.RunWithExplicitTenantAsync(
            ApiFactory.TestTenantB,
            ct => periodProvider.GetCurrentPeriodAsync(ct));
        currentB.Should().BeNull("Tenant B has no period covering today");
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

    private sealed record CreatePeriodIdResponse(Guid Id);
}
