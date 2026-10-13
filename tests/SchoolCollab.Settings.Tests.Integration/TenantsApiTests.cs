using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Tests.Integration;

/// <summary>
/// The default-tenant endpoints against real PostgreSQL.
/// </summary>
/// <remarks>
/// The unit tests run on the in-memory provider, which does not enforce FILTERED unique
/// indexes — so the "at most one platform default" invariant is only actually proven here,
/// where <c>ix_tenants_is_default_unique</c> exists. That is the reason this class exists at
/// all rather than being folded into the unit suite.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class TenantsApiTests
{
    private static ApiFactory _factory = default!;
    private static HttpClient _client = default!;

    private Tenant _alpha = default!;
    private Tenant _beta = default!;

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
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();

        // Preferences are keyed by subject, and the principal is the same synthetic TestAuth
        // user in every test — so this table must be emptied, not merely left alone.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE admin_user_preferences;");
        await db.Database.ExecuteSqlRawAsync("UPDATE tenants SET is_default = false;");

        // Fresh names each run: the registry has no delete path, and the unique name index
        // would collide on a re-run of the same database.
        _alpha = Tenant.Create($"Alpha-{Guid.NewGuid():N}", TenantType.School);
        _beta = Tenant.Create($"Beta-{Guid.NewGuid():N}", TenantType.School);
        db.Tenants.AddRange(_alpha, _beta);
        await db.SaveChangesAsync();
    }

    private static async Task<int> DefaultCountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        return await db.Tenants.CountAsync(t => t.IsDefault);
    }

    // ── Platform default ────────────────────────────────────────────────────

    [TestMethod]
    public async Task PUT_TenantDefault_DesignatesIt()
    {
        var response = await _client.PutAsync($"/api/tenants/{_alpha.Id}/default", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DefaultCountAsync()).Should().Be(1);
    }

    /// <summary>
    /// The invariant the partial unique index exists for: designating a second tenant must
    /// clear the first, in one unit of work, without ever leaving two rows flagged.
    /// </summary>
    [TestMethod]
    public async Task PUT_TenantDefault_MovesTheDesignation_LeavingExactlyOne()
    {
        await _client.PutAsync($"/api/tenants/{_alpha.Id}/default", content: null);
        var response = await _client.PutAsync($"/api/tenants/{_beta.Id}/default", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DefaultCountAsync()).Should().Be(1);

        var tenants = await _client.GetFromJsonAsync<TenantDto[]>("/api/tenants");
        tenants!.Single(t => t.Id == _beta.Id).IsDefault.Should().BeTrue();
        tenants.Single(t => t.Id == _alpha.Id).IsDefault.Should().BeFalse();
    }

    [TestMethod]
    public async Task PUT_TenantDefault_ForAnUnknownTenant_Is404()
    {
        var response = await _client.PutAsync($"/api/tenants/{Guid.NewGuid()}/default", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Operator preference ──────────────────────────────────────────────────

    [TestMethod]
    public async Task GET_MyDefaultTenant_WithNothingRecorded_ReportsNullNotNotFound()
    {
        var response = await _client.GetAsync("/api/tenants/me/default");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a missing preference is a state, not a missing resource — the portal has one shape to handle");

        var body = await response.Content.ReadFromJsonAsync<UserTenantPreferenceDto>();
        body!.DefaultTenantId.Should().BeNull();
    }

    [TestMethod]
    public async Task PUT_MyDefaultTenant_ThenGET_RoundTrips()
    {
        var put = await _client.PutAsJsonAsync("/api/tenants/me/default", new { TenantId = _beta.Id });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var body = await _client.GetFromJsonAsync<UserTenantPreferenceDto>("/api/tenants/me/default");
        body!.DefaultTenantId.Should().Be(_beta.Id);
    }

    [TestMethod]
    public async Task PUT_MyDefaultTenant_WithNull_ClearsIt()
    {
        await _client.PutAsJsonAsync("/api/tenants/me/default", new { TenantId = _beta.Id });

        var clear = await _client.PutAsJsonAsync("/api/tenants/me/default", new { TenantId = (Guid?)null });
        clear.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var body = await _client.GetFromJsonAsync<UserTenantPreferenceDto>("/api/tenants/me/default");
        body!.DefaultTenantId.Should().BeNull("clearing falls back to the platform default");
    }

    /// <summary>
    /// <see cref="Guid.Empty"/> is the system / no-tenant context. It is absent from the tenant
    /// registry by design, so the validation must not treat it as an unknown tenant — the same
    /// distinction the whole precedence chain turns on.
    /// </summary>
    [TestMethod]
    public async Task PUT_MyDefaultTenant_AcceptsTheSystemContextSentinel()
    {
        var response = await _client.PutAsJsonAsync("/api/tenants/me/default", new { TenantId = Guid.Empty });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var body = await _client.GetFromJsonAsync<UserTenantPreferenceDto>("/api/tenants/me/default");
        body!.DefaultTenantId.Should().Be(Guid.Empty,
            "preferring the shared-blueprint context is a recorded preference, not the absence of one");
    }

    [TestMethod]
    public async Task PUT_MyDefaultTenant_ForAnUnknownTenant_Is404()
    {
        var response = await _client.PutAsJsonAsync("/api/tenants/me/default", new { TenantId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task PUT_MyDefaultTenant_IsIdempotent_OnRepeat()
    {
        await _client.PutAsJsonAsync("/api/tenants/me/default", new { TenantId = _beta.Id });
        var second = await _client.PutAsJsonAsync("/api/tenants/me/default", new { TenantId = _beta.Id });

        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        (await db.AdminUserPreferences.CountAsync()).Should().Be(1,
            "the unique index on subject_id keeps it to one row per operator");
    }
}
