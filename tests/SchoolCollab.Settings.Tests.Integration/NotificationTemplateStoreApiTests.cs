extern alias migration;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;
using EntityCodeRuleSeeder = migration::SchoolCollab.MigrationService.Seeding.EntityCodeRuleSeeder;

namespace SchoolCollab.Settings.Tests.Integration;

/// <summary>
/// The notification-template store's two invariants that only real PostgreSQL can prove (naming spec
/// D6/D7): the hybrid tenant filter on the reads that feed adopt, and the <c>(tenant_id, code)</c> unique
/// index filtered to live rows that carry a code.
/// </summary>
/// <remarks>
/// <para>
/// The unit suite runs on the in-memory provider with a session that owns every row it writes, so it
/// cannot stage "another tenant already holds an identical row" and cannot see a filtered unique index
/// at all — the in-memory provider enforces neither query filters against a second scope's rows nor the
/// index. That is the reason this class exists rather than another handler test.
/// </para>
/// <para>
/// The session is <see cref="ApiFactory.TestTenant"/> (TestAuth's configured tenant); rows for the
/// second tenant are written through
/// <see cref="ITenantContextAccessor.RunWithExplicitTenantAsync"/>, the sanctioned way to act in another
/// scope.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public class NotificationTemplateStoreApiTests
{
    private const string BasePath = "/api/settings/notification-templates";
    private const string CodeIndexName = "ix_tenant_notification_templates_tenant_code";

    /// <summary>A second tenant, with no relationship to the session's own scope.</summary>
    private static readonly Guid OtherTenant = Guid.Parse("00000000-0000-0000-0000-000000000002");

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
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();

        // Per-test isolation (the sibling suites' TRUNCATE pattern): the store, its audit trail and the
        // entity-code rules the handlers mint codes from.
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE template_audit_entries, template_constraints, tenant_notification_templates, "
            + "entity_code_segments, entity_code_rules CASCADE;");

        // The create/adopt path mints a code through the shipped rule, seeded by the production seeder so
        // the rule's shape is stated once (NotificationTemplateCodeRuleTests pins that shape).
        await new EntityCodeRuleSeeder(db, accessor, NullLogger<EntityCodeRuleSeeder>.Instance)
            .SeedAsync();
    }

    /// <summary>The session's tenant, stamped the way TestAuth reads it (the <c>x-tenant-id</c> header).</summary>
    private static readonly string SessionTenantHeader = ApiFactory.TestTenant.ToString("D");

    [TestMethod]
    public async Task Adopt_Succeeds_WhenAnIdenticalRowExistsInAnotherTenant()
    {
        // Arrange — the blueprint, and an identical row in ANOTHER tenant: same kind, channel and
        // constraint set, different scope. Adopting into the session's tenant must not see it (§3.9: a
        // tenant's row shadows the blueprint for THAT tenant only).
        var blueprintId = await InsertTemplateAsync(
            tenantId: null,
            title: "System reminder",
            code: null,
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);

        var otherTenantCopyId = await InsertTemplateAsync(
            tenantId: OtherTenant,
            title: "Other tenant's copy",
            code: "TPLB1",
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);

        // Act
        var response = await SendAsSessionTenantAsync(
            HttpMethod.Post, $"{BasePath}/{blueprintId:D}/adopt?reason=Adopted%20from%20the%20blueprint");

        // Assert — a hybrid filter that leaked the other tenant's row would make this a 409, and every
        // school but the first would be unable to adopt the same blueprint.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var adopted = await response.Content.ReadFromJsonAsync<TenantNotificationTemplateDto>();
        adopted.Should().NotBeNull();
        adopted!.TenantId.Should().Be(ApiFactory.TestTenant, "the copy lands in the session's own scope");
        adopted.Title.Should().Be("System reminder", "the readable name travels with the copy (D3)");
        adopted.Code.Should().NotBeNullOrWhiteSpace().And.NotBe("TPLB1",
            "the copy mints its own code, so two live rows never share one (D7)");

        // …and the other tenant's row is not merely ignored by the conflict check: it is invisible to
        // this session, which is what makes a read-by-id a 404 rather than a 403.
        (await SendAsSessionTenantAsync(HttpMethod.Get, $"{BasePath}/{otherTenantCopyId:D}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task TwoLiveRowsInOneScope_CannotShareACode()
    {
        // Arrange — one live row, then a second in the SAME scope claiming its code.
        await InsertTemplateAsync(tenantId: ApiFactory.TestTenant, title: "First", code: "TPL01");

        // Act
        var act = () => InsertTemplateAsync(tenantId: ApiFactory.TestTenant, title: "Second", code: "TPL01");

        // Assert — the filtered unique index is what makes a code an exact reference (D6); the route
        // never mints a duplicate, so the index is the guard against a future writer that does.
        await act.Should().ThrowAsync<DbUpdateException>(
                "the (tenant_id, code) index is unique over live rows")
            .Where(ex => ex.InnerException!.Message.Contains(CodeIndexName, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ASoftDeletedRow_DoesNotBlockItsCode()
    {
        // Arrange — a row is reset (soft-deleted); its code leaves the index with it.
        var resetId = await InsertTemplateAsync(
            tenantId: ApiFactory.TestTenant, title: "Superseded", code: "TPL01");
        await SoftDeleteAsync(ApiFactory.TestTenant, resetId);

        // Act
        var reused = await InsertTemplateAsync(
            tenantId: ApiFactory.TestTenant, title: "Replacement", code: "TPL01");

        // Assert — the index is filtered to live rows, so a tombstoned row frees its code exactly as it
        // frees its (kind, channel) slot.
        reused.Should().NotBe(resetId);
        (await LiveTemplateCodesAsync()).Should().ContainSingle()
            .Which.Should().Be("TPL01", "only the live row holds the code");
    }

    /// <summary>
    /// Sends one request as the session's own tenant. <c>x-tenant-id</c> is the header TestAuth reads
    /// ahead of every default, so the request's scope is this test's, not whatever the dev switcher last
    /// resolved.
    /// </summary>
    private static Task<HttpResponseMessage> SendAsSessionTenantAsync(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("x-tenant-id", SessionTenantHeader);
        return _client.SendAsync(request);
    }

    /// <summary>Inserts one template row in <paramref name="tenantId"/>'s scope and returns its id.</summary>
    private static async Task<Guid> InsertTemplateAsync(
        Guid? tenantId,
        string title,
        string? code,
        params (TemplateConstraintDimension Dimension, string Value)[] constraints)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();

        var template = await accessor.RunWithExplicitTenantAsync(tenantId, async ct =>
        {
            var row = TenantNotificationTemplate.Create(
                tenantId: tenantId,
                kind: NotificationTemplateKind.Reminder,
                channel: NotificationChannel.SMS,
                title: title,
                description: null,
                code: code,
                subject: null,
                body: $"Body for {title}",
                constraints: constraints.Length == 0 ? null : constraints);

            db.TenantNotificationTemplates.Add(row);
            await db.SaveChangesAsync(ct);
            return row;
        });

        return template.Id;
    }

    /// <summary>Soft-deletes one row in its own scope — the store's Reset.</summary>
    private static async Task SoftDeleteAsync(Guid tenantId, Guid templateId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();

        await accessor.RunWithExplicitTenantAsync(tenantId, async ct =>
        {
            var row = await db.TenantNotificationTemplates.SingleAsync(x => x.Id == templateId, ct);
            row.MarkAsDeleted();
            await db.SaveChangesAsync(ct);
            return row.Id;
        });
    }

    /// <summary>The codes of the session's live rows — the index's own view of the scope.</summary>
    private static async Task<string[]> LiveTemplateCodesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();

        return await accessor.RunWithExplicitTenantAsync(ApiFactory.TestTenant, async ct =>
            await db.TenantNotificationTemplates
                .Where(x => x.Code != null)
                .Select(x => x.Code!)
                .ToArrayAsync(ct));
    }
}
