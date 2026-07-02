using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Config.Core.Data;

namespace SchoolCollab.Config.Tests.Integration;

/// <summary>
/// End-to-end Config API tests against ephemeral Testcontainers Postgres +
/// RabbitMQ (see <see cref="ApiFactory"/>). Covers: flag CRUD, audit-on-mutation,
/// tenant override upsert, and the consumer resolve endpoints
/// (<c>/api/features/global</c> + <c>/api/features/{tenant}</c>).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ConfigApiTests
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
        if (_factory is not null) await _factory.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConfigDbContext>();
        // Order respects FKs: overrides → flags; audit + outbox are independent.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE flag_audit_entries;");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE tenant_flag_overrides;");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE feature_flags CASCADE;");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox_messages;");
    }

    [TestMethod]
    public async Task POST_CreatesFlag_And_ListReturnsIt()
    {
        var key = $"FEATURE:PW{Guid.NewGuid():N}".ToUpperInvariant();
        var create = await _client.PostAsJsonAsync("/api/config/flags", new
        {
            Key = key,
            Name = "PW flag",
            Description = (string?)null,
            IsEnabled = true,
            Reason = "integration test",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await _client.GetFromJsonAsync<FeatureFlagDto[]>("/api/config/flags");
        list.Should().Contain(f => f.Key == key && f.IsEnabled);
    }

    [TestMethod]
    public async Task PUT_SetEnabled_WritesAuditRow()
    {
        var key = await CreateFlagAsync(enabled: true);

        var resp = await _client.PutAsJsonAsync($"/api/config/flags/{key}/enabled",
            new { IsEnabled = false, Reason = "turn off in test" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var flag = await _client.GetFromJsonAsync<FeatureFlagDto>($"/api/config/flags/{key}");
        flag!.IsEnabled.Should().BeFalse();

        var audit = await _client.GetFromJsonAsync<FlagAuditEntryDto[]>(
            $"/api/config/audit?key={key}&skip=0&take=50");
        audit.Should().Contain(a =>
            a.FeatureFlagKey == key &&
            a.PreviousIsEnabled == true &&
            a.NewIsEnabled == false &&
            a.Reason == "turn off in test" &&
            a.ActorId == "test-user-id");
    }

    [TestMethod]
    public async Task Resolve_Global_ReturnsDefault_And_TenantOverrideWins()
    {
        var key = await CreateFlagAsync(enabled: true);

        // Global resolve sees the default (true).
        var global = await _client.GetFromJsonAsync<ResolvedFlagDto[]>("/api/features/global");
        global.Should().Contain(r => r.Key == key && r.IsEnabled && r.Source == "GlobalDefault");

        // Pin a tenant override to false.
        var upsert = await _client.PutAsJsonAsync(
            $"/api/config/flags/{key}/overrides/{ApiFactory.TestTenant}",
            new { IsEnabled = false, Reason = "no AI entitlement", EffectiveFrom = (DateTimeOffset?)null, EffectiveTo = (DateTimeOffset?)null });
        upsert.StatusCode.Should().Be(HttpStatusCode.OK);

        // Tenant-scoped resolve now returns the override (false).
        var tenant = await _client.GetFromJsonAsync<ResolvedFlagDto[]>($"/api/features/{ApiFactory.TestTenant}");
        tenant.Should().Contain(r => r.Key == key && !r.IsEnabled && r.Source == "TenantOverride");

        // Global is unchanged.
        var globalAfter = await _client.GetFromJsonAsync<ResolvedFlagDto[]>("/api/features/global");
        globalAfter.Should().Contain(r => r.Key == key && r.IsEnabled);
    }

    [TestMethod]
    public async Task TenantOverrides_List_ReturnsOverrideAcrossTenants()
    {
        var key = await CreateFlagAsync(enabled: true);
        var otherTenant = Guid.NewGuid();

        await _client.PutAsJsonAsync(
            $"/api/config/flags/{key}/overrides/{ApiFactory.TestTenant}",
            new { IsEnabled = false, Reason = "test tenant off", EffectiveFrom = (DateTimeOffset?)null, EffectiveTo = (DateTimeOffset?)null });
        await _client.PutAsJsonAsync(
            $"/api/config/flags/{key}/overrides/{otherTenant}",
            new { IsEnabled = true, Reason = "other tenant on", EffectiveFrom = (DateTimeOffset?)null, EffectiveTo = (DateTimeOffset?)null });

        // The list endpoint must ignore the request tenant filter and return ALL
        // overrides for the flag (admin cross-tenant view).
        var overrides = await _client.GetFromJsonAsync<TenantFlagOverrideDto[]>(
            $"/api/config/flags/{key}/overrides");
        overrides.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task Archive_ExcludesFlagFromResolution()
    {
        var key = await CreateFlagAsync(enabled: true);

        var archive = await _client.PostAsJsonAsync($"/api/config/flags/{key}/archive",
            new { Reason = "retired" });
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resolved = await _client.GetFromJsonAsync<ResolvedFlagDto[]>("/api/features/global");
        resolved.Should().NotContain(r => r.Key == key);
    }

    private async Task<string> CreateFlagAsync(bool enabled)
    {
        var key = $"FEATURE:PW{Guid.NewGuid():N}".ToUpperInvariant();
        var resp = await _client.PostAsJsonAsync("/api/config/flags", new
        {
            Key = key,
            Name = "PW flag",
            Description = (string?)null,
            IsEnabled = enabled,
            Reason = "seed for test",
        });
        resp.EnsureSuccessStatusCode();
        return key;
    }

    // Local DTO mirrors of the API response shapes (JSON property names).
    private sealed record FeatureFlagDto(string Key, string Name, bool IsEnabled, int OverrideCount);
    private sealed record FlagAuditEntryDto(string FeatureFlagKey, bool? PreviousIsEnabled, bool? NewIsEnabled, string? Reason, string ActorId);
    private sealed record ResolvedFlagDto(string Key, bool IsEnabled, string Source);
    private sealed record TenantFlagOverrideDto(Guid TenantId);
}