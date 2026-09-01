using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Draft-period delete endpoint (documents/specs/period-draft-delete.md FR-D8).
/// Covers 204/404/422, other-tenant 404, and the DB-level ON DELETE CASCADE for a
/// Draft year's sub-periods (AC-D2/D3/D5/D7) against a real Postgres.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PeriodDeleteEndpointTests
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

    // AC-D7 / FR-D8: a valid Draft id deletes with 204; repeating the same call returns 404.
    [TestMethod]
    public async Task Delete_DraftYear_204_ThenRepeat_404()
    {
        var yearId = await CreateYearAsync(ApiFactory.TestTenantA, "AY2026", AcademicYearDivision.None);

        var first = await DeleteAsync($"/students/periods/{yearId}", ApiFactory.TestTenantA);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await DeleteAsync($"/students/periods/{yearId}", ApiFactory.TestTenantA);
        second.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "re-deleting an already-deleted period is an idempotent 404 (AC-D7)");
    }

    // FR-D8: an unknown id returns 404.
    [TestMethod]
    public async Task Delete_UnknownId_404()
    {
        var response = await DeleteAsync($"/students/periods/{Guid.NewGuid()}", ApiFactory.TestTenantA);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // AC-D5: another tenant's Draft id returns 404 and the row is untouched.
    [TestMethod]
    public async Task Delete_OtherTenantDraftId_404_RowUntouched()
    {
        var yearId = await CreateYearAsync(ApiFactory.TestTenantA, "AY2026", AcademicYearDivision.None);

        var response = await DeleteAsync($"/students/periods/{yearId}", ApiFactory.TestTenantB);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the tenant query filter hides tenant A's row from tenant B (AC-D5)");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        var count = await accessor.RunWithExplicitTenantAsync(
            ApiFactory.TestTenantA, ct => db.Periods.CountAsync(p => p.Id == yearId, ct));
        count.Should().Be(1, "tenant A's row is untouched");
    }

    // FR-D2: deleting an Active period returns 422 with a message body.
    [TestMethod]
    public async Task Delete_ActivePeriod_422_WithMessage()
    {
        var yearId = await CreateYearAsync(ApiFactory.TestTenantA, "AY2026", AcademicYearDivision.None);

        // Activate the year via its own endpoint.
        var act = await PostAsync($"/students/periods/{yearId}/activate", ApiFactory.TestTenantA, null);
        act.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await DeleteAsync($"/students/periods/{yearId}", ApiFactory.TestTenantA);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<MessageBody>();
        body!.Message.Should().Contain("Only Draft periods can be deleted");
    }

    // AC-D3 / FR-D3 / NFR-D1: a Draft year with an Active sub aborts the whole delete (422) and
    // leaves zero partial deletions at the DB level.
    [TestMethod]
    public async Task Delete_Year_ActiveSubPeriod_422_CascadeAborted()
    {
        // Build the impossible-in-production state directly: a Draft year with one Draft
        // and one Active sub (only constructible by DB manipulation).
        var yearId = await CreateYearAsync(ApiFactory.TestTenantA, "AY2026", AcademicYearDivision.Terms);
        var t1 = await CreateSubAsync(ApiFactory.TestTenantA, "T1", yearId, new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31));
        var t2 = await CreateSubAsync(ApiFactory.TestTenantA, "T2", yearId, new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
            var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
            await accessor.RunWithExplicitTenantAsync(ApiFactory.TestTenantA, async _ =>
            {
                var active = await db.Periods.SingleAsync(p => p.Id == t2);
                active.Activate();
                await db.SaveChangesAsync();
                return true;
            });
        }

        var response = await DeleteAsync($"/students/periods/{yearId}", ApiFactory.TestTenantA);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<MessageBody>();
        body!.Message.Should().Contain("T2");
        body!.Message.Should().Contain("Active");

        using var check = _factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var checkAccessor = check.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        var remaining = await checkAccessor.RunWithExplicitTenantAsync(
            ApiFactory.TestTenantA,
            ct => checkDb.Periods.CountAsync(p => p.Id == yearId || p.ParentPeriodId == yearId, ct));
        remaining.Should().Be(3,
            "zero partial deletions — year + both subs remain (AC-D3/NFR-D1)");
    }

    // AC-D2: deleting a Draft year with Draft subs physically removes all rows via the DB
    // ON DELETE CASCADE (no EF-tracked shortcut).
    [TestMethod]
    public async Task Delete_Year_WithDraftSubs_DbCascadeRemovesAll()
    {
        var yearId = await CreateYearAsync(ApiFactory.TestTenantA, "AY2026", AcademicYearDivision.Terms);
        var t1 = await CreateSubAsync(ApiFactory.TestTenantA, "T1", yearId, new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31));
        var t2 = await CreateSubAsync(ApiFactory.TestTenantA, "T2", yearId, new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30));

        var response = await DeleteAsync($"/students/periods/{yearId}", ApiFactory.TestTenantA);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        (await db.Periods.CountAsync(p => p.Id == yearId || p.ParentPeriodId == yearId)).Should().Be(0,
            "the year and both Draft subs are physically gone via ON DELETE CASCADE (AC-D2)");
    }

    // FR-D5 parity: after delete, the list no longer returns the row and the tenant cache is flushed.
    [TestMethod]
    public async Task Delete_RemovedRow_IsGoneFromListAndTenantCacheFlushed()
    {
        var yearId = await CreateYearAsync(ApiFactory.TestTenantA, "AY2026", AcademicYearDivision.None);

        var before = (await (await GetAsync("/students/periods", ApiFactory.TestTenantA)).Content
            .ReadFromJsonAsync<PeriodDto[]>())!;
        before.Should().ContainSingle(p => p.Id == yearId);

        var response = await DeleteAsync($"/students/periods/{yearId}", ApiFactory.TestTenantA);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = (await (await GetAsync("/students/periods", ApiFactory.TestTenantA)).Content
            .ReadFromJsonAsync<PeriodDto[]>())!;
        after.Should().NotContain(p => p.Id == yearId,
            "the deleted row is gone from the list and the cache was flushed (FR-D5)");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<Guid> CreateYearAsync(Guid tenantId, string name, AcademicYearDivision division)
    {
        var response = await PostAsync("/students/periods", tenantId,
            new CreatePeriod(name, new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), division));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreatePeriodIdResponse>();
        return result!.Id;
    }

    private async Task<Guid> CreateSubAsync(Guid tenantId, string name, Guid yearId, DateOnly startDate, DateOnly endDate)
    {
        var response = await PostAsync("/students/periods", tenantId,
            new CreatePeriod(name, startDate, endDate,
                AcademicYearDivision.Terms, ParentPeriodId: yearId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreatePeriodIdResponse>();
        return result!.Id;
    }

    private Task<HttpResponseMessage> GetAsync(string path, Guid tenantId) =>
        SendAsync(HttpMethod.Get, path, tenantId);

    private Task<HttpResponseMessage> DeleteAsync(string path, Guid tenantId) =>
        SendAsync(HttpMethod.Delete, path, tenantId);

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

    private sealed record CreatePeriodIdResponse(Guid Id, IReadOnlyList<Guid> SubPeriodIds);
    private sealed record MessageBody(string Message);
}
