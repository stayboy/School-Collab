using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.AdoptTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.CreateTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.PromoteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpdateTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplateById;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplates;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// The hybrid store (redesign spec 3.7-3.9 / section 6): a real tenant owns its rows, the system session
/// owns the blueprint (stored as NULL), a tenant reads the blueprint but may never write it, and
/// adopt/promote are one-way copies gated by scope.
/// <para>
/// Every assertion here is a row of the spec section 6 table or a clause of 3.8 - the point is that the
/// refusal is a typed 403/409, never a silent stamp or a duplicate.
/// </para>
/// </summary>
[TestClass]
public class NotificationTemplateOwnershipTests
{
    private SettingsDbContext _db = default!;
    private TenantProvider _tenants = default!;
    private CreateTenantNotificationTemplateHandler _create = default!;
    private UpdateTenantNotificationTemplateHandler _update = default!;
    private DeleteTenantNotificationTemplateHandler _delete = default!;
    private GetTenantNotificationTemplatesHandler _list = default!;
    private GetTenantNotificationTemplateByIdHandler _byId = default!;
    private AdoptTenantNotificationTemplateHandler _adopt = default!;
    private PromoteTenantNotificationTemplateHandler _promote = default!;

    private Guid _tenantA;
    private Guid _tenantB;

    [TestInitialize]
    public async Task Setup()
    {
        _tenants = new TenantProvider();
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase($"NotificationTemplateOwnership_{Guid.NewGuid()}")
            .Options;

        _db = new SettingsDbContext(options, _tenants);
        var auditor = new TemplateAuditRecorder(new SystemActorAccessor("test:actor", "Test Actor"));
        var accessor = new TenantContextAccessor(_tenants);

        _create = new CreateTenantNotificationTemplateHandler(_db, _tenants, auditor);
        _update = new UpdateTenantNotificationTemplateHandler(_db, _tenants, auditor);
        _delete = new DeleteTenantNotificationTemplateHandler(_db, _tenants, auditor);
        _list = new GetTenantNotificationTemplatesHandler(_db, _tenants);
        _byId = new GetTenantNotificationTemplateByIdHandler(_db);
        _adopt = new AdoptTenantNotificationTemplateHandler(_db, _tenants, auditor);
        _promote = new PromoteTenantNotificationTemplateHandler(_db, _tenants, accessor, auditor);

        // Two registered tenants, so the owner projection has names to join.
        AsSystem();
        var a = Tenant.Create("Northfield", TenantType.School);
        var b = Tenant.Create("Southgate", TenantType.School);
        _db.Tenants.AddRange(a, b);
        await _db.SaveChangesAsync();
        _tenantA = a.Id;
        _tenantB = b.Id;
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    private void AsTenant(Guid tenantId) =>
        _tenants.SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));

    private void AsSystem() =>
        _tenants.SetTenant(new TenantContext(Guid.Empty, "System", TenantType.School));

    private Task<TenantNotificationTemplateDto> CreateAsync(
        NotificationTemplateKind kind = NotificationTemplateKind.Publish,
        NotificationChannel channel = NotificationChannel.Email,
        string body = "Body for {{Title}}",
        params (TemplateConstraintDimension Dimension, string Value)[] constraints) =>
        _create.HandleAsync(new CreateTenantNotificationTemplate(
            new CreateTenantNotificationTemplateRequest(
                kind, channel, "Subject", body,
                constraints.Select(c => new TemplateConstraintRequest(c.Dimension, c.Value)).ToList(),
                "test"),
            "test"));

    private Task<TenantNotificationTemplateDto> AdoptAsync(Guid id) =>
        _adopt.HandleAsync(new AdoptTenantNotificationTemplate(id, "adopt"));

    private Task<TenantNotificationTemplateDto> PromoteAsync(Guid id) =>
        _promote.HandleAsync(new PromoteTenantNotificationTemplate(id, "promote"));
    // ── Scope stamping and visibility ──────────────────────────────────────

    [TestMethod]
    public async Task Create_InSystemScope_StoresTheBlueprintAndEveryTenantSeesIt()
    {
        AsSystem();

        var created = await CreateAsync();

        created.TenantId.Should().BeNull("the blueprint is stored as NULL, never as Guid.Empty");
        created.OwnerName.Should().Be("System");

        AsTenant(_tenantA);
        var seen = await _byId.HandleAsync(new GetTenantNotificationTemplateById(created.Id));

        seen.Should().NotBeNull();
        seen!.TenantId.Should().BeNull();
        seen.OwnerName.Should().Be("System");
    }

    [TestMethod]
    public async Task Create_InTenantScope_IsInvisibleToAnotherTenant()
    {
        AsTenant(_tenantA);
        var created = await CreateAsync();

        created.TenantId.Should().Be(_tenantA);
        created.OwnerName.Should().Be("Northfield");

        AsTenant(_tenantB);
        var foreign = await _byId.HandleAsync(new GetTenantNotificationTemplateById(created.Id));

        foreign.Should().BeNull("a foreign row and an absent row are the same 404");
        (await _list.HandleAsync(new GetTenantNotificationTemplates()))
            .Should().BeEmpty();
    }

    [TestMethod]
    public async Task List_InTenantScope_ReturnsOwnRowsPlusTheBlueprint()
    {
        AsSystem();
        await CreateAsync();

        AsTenant(_tenantA);
        var mine = await CreateAsync(constraints: (TemplateConstraintDimension.GradeLevel, "Grade 3"));

        var rows = await _list.HandleAsync(new GetTenantNotificationTemplates());

        rows.Should().HaveCount(2);
        rows.Select(r => r.Id).Should().BeEquivalentTo([mine.Id, rows.Single(r => r.TenantId is null).Id]);
        rows.Should().Contain(r => r.TenantId == _tenantA && r.OwnerName == "Northfield");
    }

    [TestMethod]
    public async Task List_IncludeTenantOwned_IsHonouredInSystemScope_AndIgnoredInATenantSession()
    {
        AsTenant(_tenantA);
        await CreateAsync();

        // The tenant asks for every tenant's rows: the flag must not widen its view.
        (await _list.HandleAsync(new GetTenantNotificationTemplates(IncludeTenantOwned: true)))
            .Should().HaveCount(1)
            .And.OnlyContain(r => r.TenantId == _tenantA);

        AsSystem();
        (await _list.HandleAsync(new GetTenantNotificationTemplates()))
            .Should().BeEmpty("without the flag, system scope sees the blueprint only");

        (await _list.HandleAsync(new GetTenantNotificationTemplates(IncludeTenantOwned: true)))
            .Should().HaveCount(1)
            .And.OnlyContain(r => r.TenantId == _tenantA);
    }

    [TestMethod]
    public async Task Defaults_ArePerScope_SoATenantDefaultSitsBesideTheSystemDefault()
    {
        AsSystem();
        await CreateAsync();

        AsTenant(_tenantA);
        var mine = await CreateAsync();
        mine.TenantId.Should().Be(_tenantA, "the two defaults are in different scopes, so neither conflicts");

        var act = () => CreateAsync();
        await act.Should().ThrowAsync<TenantNotificationTemplateConflictException>();
    }
    // ── Write refusals (spec section 6) ───────────────────────────────────

    [TestMethod]
    public async Task Update_ASystemRowFromATenantSession_Is403AndLeavesTheRowUnchanged()
    {
        AsSystem();
        var system = await CreateAsync(body: "BLUEPRINT BODY");

        AsTenant(_tenantA);
        var act = () => _update.HandleAsync(new UpdateTenantNotificationTemplate(
            system.Id,
            new UpdateTenantNotificationTemplateRequest("Subject", "HIJACKED", [], "attempt"),
            "attempt"));

        await act.Should().ThrowAsync<TenantNotificationTemplateScopeException>();

        var row = await _db.TenantNotificationTemplates.IgnoreQueryFilters(["Tenant"])
            .AsNoTracking().SingleAsync(t => t.Id == system.Id);
        row.Body.Should().Be("BLUEPRINT BODY", "a refused write never reaches the store");
        row.TenantId.Should().BeNull();
    }

    [TestMethod]
    public async Task Delete_ASystemRowFromATenantSession_Is403AndLeavesTheRowLive()
    {
        AsSystem();
        var system = await CreateAsync();

        AsTenant(_tenantA);
        var act = () => _delete.HandleAsync(new DeleteTenantNotificationTemplate(system.Id, "attempt"));

        await act.Should().ThrowAsync<TenantNotificationTemplateScopeException>();

        (await _db.TenantNotificationTemplates.IgnoreQueryFilters(["Tenant"])
            .AsNoTracking().SingleAsync(t => t.Id == system.Id))
            .IsDeleted.Should().BeFalse();
    }

    [TestMethod]
    public async Task Writes_RejectGuidEmptyAsAScope()
    {
        var act = () => Task.FromResult(TenantNotificationTemplate.Create(
            Guid.Empty, NotificationTemplateKind.Publish, NotificationChannel.Email, "s", "b"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Adopt (spec 3.8) ─────────────────────────────────────────────────

    [TestMethod]
    public async Task Adopt_CopiesFieldsAndConstraints_AndNeverMutatesTheSource()
    {
        AsSystem();
        var system = await CreateAsync(
            body: "BLUEPRINT BODY",
            constraints: (TemplateConstraintDimension.GradeLevel, "Grade 3"));

        AsTenant(_tenantA);
        var copy = await AdoptAsync(system.Id);

        copy.Id.Should().NotBe(system.Id);
        copy.TenantId.Should().Be(_tenantA);
        copy.OwnerName.Should().Be("Northfield");
        copy.Body.Should().Be("BLUEPRINT BODY");
        copy.Constraints.Should().ContainSingle()
            .Which.Value.Should().Be("Grade 3");

        var source = await _db.TenantNotificationTemplates.IgnoreQueryFilters(["Tenant"])
            .Include(t => t.Constraints)
            .AsNoTracking().SingleAsync(t => t.Id == system.Id);
        source.TenantId.Should().BeNull("adopt never moves or re-points the blueprint row");
    }

    [TestMethod]
    public async Task Adopt_Twice_IsAConflictOnTheIdenticalConstraintSet()
    {
        AsTenant(_tenantA);
        var system = await CreateAsync(constraints: (TemplateConstraintDimension.GradeLevel, "Grade 3"));
        // Re-create the same set as the blueprint, so the source is adoptable.
        AsSystem();
        var blueprint = await CreateAsync(constraints: (TemplateConstraintDimension.GradeLevel, "Grade 3"));

        AsTenant(_tenantA);
        var act = () => AdoptAsync(blueprint.Id);

        await act.Should().ThrowAsync<TenantNotificationTemplateConflictException>();
        system.Id.Should().NotBe(blueprint.Id);
    }

    [TestMethod]
    public async Task Adopt_FromSystemScope_Is403()
    {
        AsSystem();
        var system = await CreateAsync();

        var act = () => AdoptAsync(system.Id);

        await act.Should().ThrowAsync<TenantNotificationTemplateScopeException>();
    }

    [TestMethod]
    public async Task Adopt_ATenantOwnedRow_IsAConflict()
    {
        AsTenant(_tenantB);
        var own = await CreateAsync();

        AsTenant(_tenantB);
        var act = () => AdoptAsync(own.Id);

        await act.Should().ThrowAsync<TenantNotificationTemplateConflictException>();
    }
    // ── Publish portal-wide (spec 3.8) ──────────────────────────────────

    [TestMethod]
    public async Task Promote_CopiesATenantRowIntoTheBlueprint_AndNeverMutatesTheSource()
    {
        AsTenant(_tenantA);
        var tenantRow = await CreateAsync(
            body: "NORTHFIELD BODY",
            constraints: (TemplateConstraintDimension.GradeLevel, "Grade 3"));

        AsSystem();
        var blueprint = await PromoteAsync(tenantRow.Id);

        blueprint.Id.Should().NotBe(tenantRow.Id);
        blueprint.TenantId.Should().BeNull();
        blueprint.OwnerName.Should().Be("System");
        blueprint.Body.Should().Be("NORTHFIELD BODY");
        blueprint.Constraints.Should().ContainSingle()
            .Which.Value.Should().Be("Grade 3");

        var source = await _db.TenantNotificationTemplates.IgnoreQueryFilters(["Tenant"])
            .Include(t => t.Constraints)
            .AsNoTracking().SingleAsync(t => t.Id == tenantRow.Id);
        source.TenantId.Should().Be(_tenantA, "promote copies; it never re-points the tenant's row");
        source.IsDeleted.Should().BeFalse();
    }

    [TestMethod]
    public async Task Promote_Twice_IsAConflictOnTheIdenticalSystemSet()
    {
        AsTenant(_tenantA);
        var tenantRow = await CreateAsync(constraints: (TemplateConstraintDimension.GradeLevel, "Grade 3"));

        AsSystem();
        await PromoteAsync(tenantRow.Id);

        var act = () => PromoteAsync(tenantRow.Id);

        await act.Should().ThrowAsync<TenantNotificationTemplateConflictException>();
    }

    [TestMethod]
    public async Task Promote_FromATenantSession_Is403()
    {
        AsTenant(_tenantA);
        var tenantRow = await CreateAsync();

        var act = () => PromoteAsync(tenantRow.Id);

        await act.Should().ThrowAsync<TenantNotificationTemplateScopeException>();
    }

    [TestMethod]
    public async Task Promote_ASystemRow_IsAConflict()
    {
        AsSystem();
        var system = await CreateAsync();

        var act = () => PromoteAsync(system.Id);

        await act.Should().ThrowAsync<TenantNotificationTemplateConflictException>();
    }

    [TestMethod]
    public async Task Promote_InSystemScope_ReadsTheTenantRowAcrossTheFilter()
    {
        // The one sanctioned cross-tenant read: a tenant's row is otherwise invisible to system scope.
        AsTenant(_tenantB);
        var tenantRow = await CreateAsync();

        AsSystem();
        (await _byId.HandleAsync(new GetTenantNotificationTemplateById(tenantRow.Id)))
            .Should().BeNull("the ordinary by-id read stays filtered");

        var blueprint = await PromoteAsync(tenantRow.Id);
        blueprint.TenantId.Should().BeNull();
    }

    // ── Audit trail carries the scope ────────────────────────────────────

    [TestMethod]
    public async Task Audit_AnAdoptIsRecordedAgainstTheTenant()
    {
        AsSystem();
        var system = await CreateAsync();

        AsTenant(_tenantA);
        var adopted = await AdoptAsync(system.Id);

        var entries = await _db.TemplateAuditEntries.AsNoTracking().ToListAsync();
        entries.Should().Contain(e => e.TemplateId == adopted.Id && e.TenantId == _tenantA);
    }

    [TestMethod]
    public async Task Audit_APromoteIsRecordedAgainstTheBlueprint()
    {
        AsTenant(_tenantA);
        var tenantRow = await CreateAsync(constraints: (TemplateConstraintDimension.GradeLevel, "Grade 3"));

        AsSystem();
        var promoted = await PromoteAsync(tenantRow.Id);

        var entries = await _db.TemplateAuditEntries.AsNoTracking().ToListAsync();
        entries.Should().Contain(e => e.TemplateId == promoted.Id && e.TenantId == null,
            "a blueprint mutation has no tenant to record");
    }
}