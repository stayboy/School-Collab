using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpsertTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTemplateAuditEntries;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplates;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Notification-template store tests (assignment-notification-templates spec §3.1,
/// §3.9, §5 workstream 1). Each test exercises a validation row from spec §8 that
/// belongs to the template-store slice.
/// </summary>
[TestClass]
public class NotificationTemplateHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private SettingsDbContext _db = default!;
    private TenantProvider _tenants = default!;
    private GetTenantNotificationTemplatesHandler _getAll = default!;
    private GetTenantNotificationTemplateHandler _getOne = default!;
    private UpsertTenantNotificationTemplateHandler _upsert = default!;
    private DeleteTenantNotificationTemplateHandler _delete = default!;
    private GetTemplateAuditEntriesHandler _audit = default!;

    [TestInitialize]
    public void Setup()
    {
        _tenants = new TenantProvider();
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase($"NotificationTemplates_{Guid.NewGuid()}")
            .Options;

        _db = new SettingsDbContext(options, _tenants);
        var auditor = new TemplateAuditRecorder(new SystemActorAccessor("test:actor", "Test Actor"));

        _getAll = new GetTenantNotificationTemplatesHandler(_db);
        _getOne = new GetTenantNotificationTemplateHandler(_db);
        _upsert = new UpsertTenantNotificationTemplateHandler(_db, _tenants, auditor);
        _delete = new DeleteTenantNotificationTemplateHandler(_db, _tenants, auditor);
        _audit = new GetTemplateAuditEntriesHandler(_db, _tenants);
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    private void AsTenant(Guid tenantId) =>
        _tenants.SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));

    private Task<TenantNotificationTemplateDto> UpsertAsync(
        NotificationTemplateKind kind,
        NotificationChannel channel,
        string body = "Body for {{Title}}",
        string? subject = "Subject for {{Title}}",
        params (TemplateConstraintDimension Dimension, string Value)[] constraints) =>
        _upsert.HandleAsync(new UpsertTenantNotificationTemplate(
            kind, channel,
            new UpsertTenantNotificationTemplateRequest(
                subject, body,
                constraints.Select(c => new UpsertTemplateConstraintRequest(c.Dimension, c.Value)).ToList(),
                "test"),
            "test"));

    // ── Get ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetOne_ReturnsNullWhenUnset()
    {
        AsTenant(TenantA);

        var result = await _getOne.HandleAsync(
            new GetTenantNotificationTemplate(NotificationTemplateKind.Publish, NotificationChannel.Email));

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task GetAll_ReturnsOnlyTheCurrentTenantsRows()
    {
        AsTenant(TenantA);
        await UpsertAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        AsTenant(TenantB);
        await UpsertAsync(NotificationTemplateKind.Reminder, NotificationChannel.SMS);

        var tenantBRows = await _getAll.HandleAsync(new GetTenantNotificationTemplates());
        tenantBRows.Should().ContainSingle();
        tenantBRows[0].Kind.Should().Be(NotificationTemplateKind.Reminder);

        AsTenant(TenantA);
        var tenantARows = await _getAll.HandleAsync(new GetTenantNotificationTemplates());
        tenantARows.Should().ContainSingle();
        tenantARows[0].Kind.Should().Be(NotificationTemplateKind.Publish);
    }

    // ── Upsert ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Upsert_CreatesRowAndConstraints_AndWritesCreatedAuditEntry()
    {
        AsTenant(TenantA);

        var created = await UpsertAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);

        created.Kind.Should().Be(NotificationTemplateKind.Publish);
        created.Channel.Should().Be(NotificationChannel.Email);
        created.Subject.Should().Be("Subject for {{Title}}");
        created.Constraints.Should().ContainSingle()
            .Which.Dimension.Should().Be(TemplateConstraintDimension.GradeLevel);

        // Every constraint row must carry the generated template id — the create
        // path used to write Guid.Empty here.
        var persisted = await _db.TemplateConstraints.AsNoTracking().SingleAsync();
        persisted.TemplateId.Should().Be(created.Id);

        var audit = await _audit.HandleAsync(
            new GetTemplateAuditEntries(NotificationTemplateKind.Publish, NotificationChannel.Email));
        audit.Should().ContainSingle();
        audit[0].ChangeKind.Should().Be(TemplateChangeKind.Created);
        audit[0].NewBody.Should().Be("Body for {{Title}}");
        audit[0].PreviousBody.Should().BeNull();
        audit[0].ActorId.Should().Be("test:actor");
    }

    [TestMethod]
    public async Task Upsert_Twice_ReplacesConstraints_AndRecordsBeforeAndAfter()
    {
        AsTenant(TenantA);

        await UpsertAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            body: "First body",
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);

        await UpsertAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            body: "Second body",
            constraints:
            [
                (TemplateConstraintDimension.GradeLevel, "Grade 4"),
                (TemplateConstraintDimension.Subject, "Mathematics"),
            ]);

        var current = await _getOne.HandleAsync(
            new GetTenantNotificationTemplate(NotificationTemplateKind.Publish, NotificationChannel.Email));

        current.Should().NotBeNull();
        current!.Body.Should().Be("Second body");
        current.Constraints.Should().HaveCount(2);

        // Replacing the set is a delete + insert, not an accumulation.
        (await _db.TemplateConstraints.AsNoTracking().CountAsync()).Should().Be(2);

        var audit = await _audit.HandleAsync(
            new GetTemplateAuditEntries(NotificationTemplateKind.Publish, NotificationChannel.Email));

        audit.Should().HaveCount(2);
        // Select by change kind rather than by index: two mutations in the same
        // DateTimeOffset tick share OccurredAt, so "most recent first" is not
        // positional-deterministic for the test fixture.
        var updated = audit.Single(a => a.ChangeKind == TemplateChangeKind.Updated);
        updated.PreviousBody.Should().Be("First body");
        updated.NewBody.Should().Be("Second body");
        updated.PreviousConstraints.Should().NotBeNull().And.Contain("Grade 3");
        updated.NewConstraints.Should().NotBeNull().And.Contain("Mathematics");
    }

    [TestMethod]
    public async Task Upsert_RejectsEmptyBody()
    {
        AsTenant(TenantA);

        var act = () => UpsertAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email, body: "   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Upsert_RejectsWhatsAppChannel()
    {
        AsTenant(TenantA);

        // WhatsApp delivers the SMS body (spec §2) — a WhatsApp template row is invalid.
        var act = () => UpsertAsync(
            NotificationTemplateKind.Publish, NotificationChannel.WhatsApp);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public async Task Upsert_TreatsNullConstraintsAsEmpty()
    {
        AsTenant(TenantA);

        var result = await _upsert.HandleAsync(new UpsertTenantNotificationTemplate(
            NotificationTemplateKind.Overdue, NotificationChannel.SMS,
            new UpsertTenantNotificationTemplateRequest(null, "Overdue: {{Title}}", null!),
            "test"));

        result.Constraints.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Upsert_DropsTheSubjectForSmsTemplates()
    {
        // Spec §5: "upsert { subject?, body, constraints } (subject ignored for SMS)".
        AsTenant(TenantA);

        var created = await UpsertAsync(
            NotificationTemplateKind.Publish, NotificationChannel.SMS,
            body: "Publish: {{Title}}",
            subject: "Subject that must not be stored");

        created.Subject.Should().BeNull();

        // An update must not reintroduce it either.
        var updated = await UpsertAsync(
            NotificationTemplateKind.Publish, NotificationChannel.SMS,
            body: "Publish v2: {{Title}}",
            subject: "Another subject");

        updated.Subject.Should().BeNull();
        updated.Body.Should().Be("Publish v2: {{Title}}");
    }

    // ── Delete / reset ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task Delete_RemovesRowAndConstraints_AndWritesDeletedAuditEntry()
    {
        AsTenant(TenantA);
        await UpsertAsync(
            NotificationTemplateKind.Reminder, NotificationChannel.Email,
            constraints: [(TemplateConstraintDimension.Stream, "Science")]);

        await _delete.HandleAsync(new DeleteTenantNotificationTemplate(
            NotificationTemplateKind.Reminder, NotificationChannel.Email, "reset"));

        var afterReset = await _getOne.HandleAsync(
            new GetTenantNotificationTemplate(NotificationTemplateKind.Reminder, NotificationChannel.Email));
        afterReset.Should().BeNull();

        (await _db.TemplateConstraints.AsNoTracking().CountAsync()).Should().Be(0);

        var audit = await _audit.HandleAsync(
            new GetTemplateAuditEntries(NotificationTemplateKind.Reminder, NotificationChannel.Email));

        audit.Should().HaveCount(2);
        var deleted = audit.Single(a => a.ChangeKind == TemplateChangeKind.Deleted);
        deleted.NewBody.Should().BeNull();
        deleted.PreviousBody.Should().NotBeNull();
    }

    [TestMethod]
    public async Task AuditTrail_SurvivesReset()
    {
        // Regression: an id-based audit join lost the whole trail once the template
        // row was soft-deleted, so a reset wiped the template's auditing history
        // from the UI (spec §3.9 requires it to be visible).
        AsTenant(TenantA);
        await UpsertAsync(NotificationTemplateKind.Completion, NotificationChannel.SMS);

        await _delete.HandleAsync(new DeleteTenantNotificationTemplate(
            NotificationTemplateKind.Completion, NotificationChannel.SMS, "reset"));

        var audit = await _audit.HandleAsync(
            new GetTemplateAuditEntries(NotificationTemplateKind.Completion, NotificationChannel.SMS));

        audit.Should().HaveCount(2);
        audit.Select(a => a.ChangeKind).Should().Contain(TemplateChangeKind.Created);
        audit.Select(a => a.ChangeKind).Should().Contain(TemplateChangeKind.Deleted);
    }

    [TestMethod]
    public async Task Delete_IsIdempotent_AndWritesNoAuditRowWhenAbsent()
    {
        AsTenant(TenantA);

        await _delete.HandleAsync(new DeleteTenantNotificationTemplate(
            NotificationTemplateKind.Publish, NotificationChannel.SMS, "reset"));

        var audit = await _audit.HandleAsync(
            new GetTemplateAuditEntries(NotificationTemplateKind.Publish, NotificationChannel.SMS));

        audit.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Upsert_AfterReset_RecreatesTheTemplate()
    {
        // The unique (tenant, kind, channel) index is filtered on is_deleted = false,
        // so a reset must not block re-creating the same kind × channel.
        AsTenant(TenantA);
        await UpsertAsync(NotificationTemplateKind.Publish, NotificationChannel.Email, body: "First");

        await _delete.HandleAsync(new DeleteTenantNotificationTemplate(
            NotificationTemplateKind.Publish, NotificationChannel.Email, "reset"));

        var recreated = await UpsertAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email, body: "Second");

        recreated.Body.Should().Be("Second");
        (await _db.TenantNotificationTemplates.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task AuditTrail_IsScopedToTheCurrentTenant()
    {
        AsTenant(TenantA);
        await UpsertAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        AsTenant(TenantB);
        var tenantBAudit = await _audit.HandleAsync(
            new GetTemplateAuditEntries(NotificationTemplateKind.Publish, NotificationChannel.Email));

        tenantBAudit.Should().BeEmpty();
    }
}
