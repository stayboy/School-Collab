using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Tests.Integration;

/// <summary>
/// Strict-tenancy of the <c>academic_year_division</c> framework setting
/// (period-hierarchy-terms-semesters.md NFR-H2 / FR-H6/H7). The setting is owned
/// by Settings; its per-tenant resolution is proven at the API boundary via the
/// <c>x-tenant-id</c> header (the Students-side provider forwards the tenant via
/// the "settings-api" named client + TenantForwardingDelegatingHandler).
/// </summary>
[TestClass]
[DoNotParallelize]
public class AcademicYearDivisionTenancyTests
{
    private static ApiFactory _factory = default!;
    private static HttpClient _client = default!;

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

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
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE flag_audit_entries;");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE tenant_flag_overrides;");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE feature_flags CASCADE;");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox_messages;");

        // Seed the global AcademicYearDivision flag (Value = "None") directly — the
        // MigrationService does not run in the test factory, so without this seed
        // GET returns 404 and PUT 404s. Mirrors SeedAcademicYearDivisionAsync.
        var key = FeatureFlag.NormalizeKey(FeatureFlagKeys.AcademicYearDivision);
        if (!await db.FeatureFlags.AnyAsync(f => f.Key == key))
        {
            db.FeatureFlags.Add(FeatureFlag.Create(
                key,
                "Academic-year division (None | Terms | Semesters)",
                "Selects the academic-calendar subdivision for activity-group enrollment spans.",
                isEnabled: true,
                kind: FlagKind.String,
                value: nameof(AcademicYearDivision.None)));
            await db.SaveChangesAsync();
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, Guid tenantId) =>
        new(method, url) { Headers = { { "x-tenant-id", tenantId.ToString() } } };

    [TestMethod]
    public async Task AcademicYearDivision_IsTenantScoped()
    {
        // Tenant A sets its division to Terms.
        var put = Request(HttpMethod.Put, "/api/config/flags/academic_year_division", TenantA);
        put.Content = JsonContent.Create(new { Value = "Terms", Reason = "test" });
        var putResp = await _client.SendAsync(put);
        putResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Tenant A reads back Terms.
        var getA = await _client.SendAsync(Request(HttpMethod.Get, "/api/config/flags/academic_year_division", TenantA));
        getA.StatusCode.Should().Be(HttpStatusCode.OK);
        var dtoA = await getA.Content.ReadFromJsonAsync<AcademicYearDivisionDto>();
        dtoA!.Value.Should().Be("Terms");
        dtoA.Source.Should().Be("TenantOverride");

        // A second tenant still resolves the global default (None) — the setting is
        // stored and resolved strictly per tenant.
        var getB = await _client.SendAsync(Request(HttpMethod.Get, "/api/config/flags/academic_year_division", TenantB));
        getB.StatusCode.Should().Be(HttpStatusCode.OK);
        var dtoB = await getB.Content.ReadFromJsonAsync<AcademicYearDivisionDto>();
        dtoB!.Value.Should().Be("None");
        dtoB.Source.Should().Be("GlobalDefault");
    }

    [TestMethod]
    public async Task AcademicYearDivision_PutRejectsInvalidValue()
    {
        var put = Request(HttpMethod.Put, "/api/config/flags/academic_year_division", TenantA);
        put.Content = JsonContent.Create(new { Value = "Quarterly", Reason = "test" });
        var resp = await _client.SendAsync(put);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the request-shape guard rejects a value outside None | Terms | Semesters");
    }
}
