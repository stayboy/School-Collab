using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.CreateTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpdateTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTemplateAuditEntries;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplates;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Notification-template store tests (assignment-notification-templates spec §3.1, §3.9, §5
/// workstream 1; round notification-templates-b2c re-shaped the writer). A tenant holds several live
/// rows per (kind, channel): the unconstrained one is the tenant default (Q15), the constrained ones
/// are matcher candidates, and every mutation is addressed <b>by id</b>. Each test exercises a
/// validation row from spec §8 that belongs to the template-store slice.
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
    private CreateTenantNotificationTemplateHandler _create = default!;
    private UpdateTenantNotificationTemplateHandler _update = default!;
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
        _create = new CreateTenantNotificationTemplateHandler(_db, _tenants, auditor);
        _update = new UpdateTenantNotificationTemplateHandler(_db, _tenants, auditor);
        _delete = new DeleteTenantNotificationTemplateHandler(_db, _tenants, auditor);
        _audit = new GetTemplateAuditEntriesHandler(_db, _tenants);
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    private void AsTenant(Guid tenantId) =>
        _tenants.SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));

    private Task<TenantNotificationTemplateDto> CreateAsync(
        NotificationTemplateKind kind,
        NotificationChannel channel,
        string body = "Body for {{Title}}",
        string? subject = "Subject for {{Title}}",
        params (TemplateConstraintDimension Dimension, string Value)[] constraints) =>
        _create.HandleAsync(new CreateTenantNotificationTemplate(
            new CreateTenantNotificationTemplateRequest(
                kind, channel, subject, body,
                constraints.Select(c => new TemplateConstraintRequest(c.Dimension, c.Value)).ToList(),
                "test"),
            "test"));

    private Task<TenantNotificationTemplateDto> UpdateAsync(
        Guid templateId,
        string body = "Body for {{Title}}",
        string? subject = "Subject for {{Title}}",
        params (TemplateConstraintDimension Dimension, string Value)[] constraints) =>
        _update.HandleAsync(new UpdateTenantNotificationTemplate(
            templateId,
            new UpdateTenantNotificationTemplateRequest(
                subject, body,
                constraints.Select(c => new TemplateConstraintRequest(c.Dimension, c.Value)).ToList(),
                "test"),
            "test"));

    private Task<TenantNotificationTemplateDto?> GetDefaultAsync(
        NotificationTemplateKind kind, NotificationChannel channel) =>
        _getOne.HandleAsync(new GetTenantNotificationTemplate(kind, channel));

    // ── Get ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetOne_ReturnsNullWhenUnset()
    {
        AsTenant(TenantA);

        var result = await GetDefaultAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task GetOne_ReturnsTheUnconstrainedRow_WhenConstrainedCandidatesExist()
    {
        // Round b2c, the discriminating case: two live rows for one slot must return the DEFAULT,
        // where the pre-B2c SingleOrDefault read threw instead.
        AsTenant(TenantA);
        var constrained = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            body: "constrained body",
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);
        var unconstrained = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email, body: "default body");

        var result = await GetDefaultAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        result.Should().NotBeNull();
        result!.Id.Should().Be(unconstrained.Id);
        result.Body.Should().Be("default body");
        result.Constraints.Should().BeEmpty();
        result.Id.Should().NotBe(constrained.Id);
    }

    [TestMethod]
    public async Task GetOne_ReturnsNull_WhenOnlyConstrainedCandidatesExist()
    {
        // 204 on the wire: no default row means the built-in default applies, even though the slot
        // is not empty.
        AsTenant(TenantA);
        await CreateAsync(
            NotificationTemplateKind.Reminder, NotificationChannel.Email,
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);

        var result = await GetDefaultAsync(NotificationTemplateKind.Reminder, NotificationChannel.Email);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task GetOne_NeverThrows_WithASecondLiveRow()
    {
        AsTenant(TenantA);
        await CreateAsync(NotificationTemplateKind.Overdue, NotificationChannel.SMS, body: "one");
        await CreateAsync(
            NotificationTemplateKind.Overdue, NotificationChannel.SMS, body: "two",
            constraints: [(TemplateConstraintDimension.Stream, "Science")]);

        var act = () => GetDefaultAsync(NotificationTemplateKind.Overdue, NotificationChannel.SMS);

        (await act.Should().NotThrowAsync()).Subject!.Body.Should().Be("one");
    }

    [TestMethod]
    public async Task GetAll_ReturnsOnlyTheCurrentTenantsRows()
    {
        AsTenant(TenantA);
        await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        AsTenant(TenantB);
        await CreateAsync(NotificationTemplateKind.Reminder, NotificationChannel.SMS);

        var tenantBRows = await _getAll.HandleAsync(new GetTenantNotificationTemplates());
        tenantBRows.Should().ContainSingle();
        tenantBRows[0].Kind.Should().Be(NotificationTemplateKind.Reminder);

        AsTenant(TenantA);
        var tenantARows = await _getAll.HandleAsync(new GetTenantNotificationTemplates());
        tenantARows.Should().ContainSingle();
        tenantARows[0].Kind.Should().Be(NotificationTemplateKind.Publish);
    }

    [TestMethod]
    public async Task GetAll_ReturnsEveryCandidateOfASlot_WithItsConstraints()
    {
        // The cross-context candidate read (Assignments side) consumes this list, so it must carry
        // every live row of a slot — not one — plus each row's constraint set and createdAt.
        AsTenant(TenantA);
        var constrained = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);
        var unconstrained = await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        var rows = await _getAll.HandleAsync(new GetTenantNotificationTemplates());

        rows.Should().HaveCount(2);
        rows.Should().Contain(r => r.Id == constrained.Id && r.Constraints.Count == 1);
        rows.Should().Contain(r => r.Id == unconstrained.Id && r.Constraints.Count == 0);
        rows.Should().OnlyContain(r => r.CreatedAt != default, "createdAt is the Q67 tie-break input");
    }

    // ── Create ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Create_WritesRowAndConstraints_AndACreatedAuditEntry()
    {
        AsTenant(TenantA);

        var created = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);

        created.Kind.Should().Be(NotificationTemplateKind.Publish);
        created.Channel.Should().Be(NotificationChannel.Email);
        created.Subject.Should().Be("Subject for {{Title}}");
        created.Constraints.Should().ContainSingle()
            .Which.Dimension.Should().Be(TemplateConstraintDimension.GradeLevel);
        created.CreatedAt.Should().NotBe(default);

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
    public async Task Create_RejectsEmptyBody()
    {
        AsTenant(TenantA);

        var act = () => CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email, body: "   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Create_RejectsWhatsAppChannel()
    {
        AsTenant(TenantA);

        // WhatsApp delivers the SMS body (spec §2) — a WhatsApp template row is invalid.
        var act = () => CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.WhatsApp);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public async Task Create_TreatsNullConstraintsAsEmpty()
    {
        AsTenant(TenantA);

        var result = await _create.HandleAsync(new CreateTenantNotificationTemplate(
            new CreateTenantNotificationTemplateRequest(
                NotificationTemplateKind.Overdue, NotificationChannel.SMS,
                Subject: null, Body: "Overdue: {{Title}}", Constraints: null!),
            "test"));

        result.Constraints.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Create_DropsTheSubjectForSmsTemplates()
    {
        // Spec §5: "subject ignored for SMS".
        AsTenant(TenantA);

        var created = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.SMS,
            body: "Publish: {{Title}}",
            subject: "Subject that must not be stored");

        created.Subject.Should().BeNull();
    }

    [TestMethod]
    public async Task Create_SecondDefaultForTheSameSlot_IsAConflict()
    {
        // Round b2c WS2 (Q15/Q66): an empty constraint set IS the tenant default, so exactly one may
        // exist per (kind, channel). Discriminating: the same call with one constraint succeeds.
        AsTenant(TenantA);
        await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        var act = () => CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        await act.Should().ThrowAsync<TenantNotificationTemplateConflictException>();
        (await _db.TenantNotificationTemplates.AsNoTracking().CountAsync()).Should().Be(1,
            "the rejected write leaves no row behind");
        (await _audit.HandleAsync(
            new GetTemplateAuditEntries(NotificationTemplateKind.Publish, NotificationChannel.Email)))
            .Should().ContainSingle("and no audit entry either");
    }

    [TestMethod]
    public async Task Create_ConstrainedCandidateAlongsideTheDefault_IsAllowed()
    {
        // The other half of the invariant: constrained rows are exactly what b2c exists for.
        AsTenant(TenantA);
        await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        var constrained = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            body: "grade 3 body",
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);

        constrained.Constraints.Should().ContainSingle();
        (await _db.TenantNotificationTemplates.AsNoTracking().CountAsync()).Should().Be(2);
    }

    [TestMethod]
    public async Task Create_SecondConstrainedCandidate_IsAllowed_AndTheDefaultStillResolves()
    {
        AsTenant(TenantA);
        await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);
        await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 4")]);
        var unconstrained = await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        (await GetDefaultAsync(NotificationTemplateKind.Publish, NotificationChannel.Email))!
            .Id.Should().Be(unconstrained.Id);
    }

    // ── Update (by id) ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task Update_ReplacesConstraints_AndRecordsBeforeAndAfter()
    {
        AsTenant(TenantA);
        var created = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            body: "First body",
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);

        await UpdateAsync(
            created.Id,
            body: "Second body",
            constraints:
            [
                (TemplateConstraintDimension.GradeLevel, "Grade 4"),
                (TemplateConstraintDimension.Subject, "Mathematics"),
            ]);

        // Read it back through the list: the row is no longer the slot's DEFAULT (it kept two
        // constraints), so the narrowed get-one read deliberately returns null here.
        var current = (await _getAll.HandleAsync(new GetTenantNotificationTemplates()))
            .Single(r => r.Id == created.Id);
        current.Body.Should().Be("Second body");
        current.Constraints.Should().HaveCount(2);
        (await GetDefaultAsync(NotificationTemplateKind.Publish, NotificationChannel.Email))
            .Should().BeNull("a constrained row is a candidate, not the tenant default");

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
    public async Task Update_DropsTheSubjectForSmsTemplates()
    {
        AsTenant(TenantA);
        var created = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.SMS,
            body: "Publish: {{Title}}",
            subject: "Subject that must not be stored");
        created.Subject.Should().BeNull();

        var updated = await UpdateAsync(
            created.Id, body: "Publish v2: {{Title}}", subject: "Another subject");

        updated.Subject.Should().BeNull();
        updated.Body.Should().Be("Publish v2: {{Title}}");
    }

    [TestMethod]
    public async Task Update_UnknownId_IsNotFound()
    {
        AsTenant(TenantA);

        var act = () => UpdateAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<TenantNotificationTemplateNotFoundException>();
    }

    [TestMethod]
    public async Task Update_AnotherTenantsRow_IsNotFound()
    {
        // The tenant query filter makes a foreign row indistinguishable from an absent one.
        AsTenant(TenantA);
        var created = await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        AsTenant(TenantB);
        var act = () => UpdateAsync(created.Id);

        await act.Should().ThrowAsync<TenantNotificationTemplateNotFoundException>();
    }

    [TestMethod]
    public async Task Update_ClearingConstraintsWhenADefaultExists_IsAConflict()
    {
        // A constrained candidate edited down to an empty set would become a SECOND default.
        AsTenant(TenantA);
        await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);
        var constrained = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);

        var act = () => UpdateAsync(constrained.Id, body: "now unconstrained");

        await act.Should().ThrowAsync<TenantNotificationTemplateConflictException>();
    }

    [TestMethod]
    public async Task Update_KeepsItsOwnDefaultRowUnconstrained()
    {
        // The exclusion is by id: updating the default itself must not collide with itself.
        AsTenant(TenantA);
        var created = await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        var updated = await UpdateAsync(created.Id, body: "default v2");

        updated.Body.Should().Be("default v2");
        updated.Constraints.Should().BeEmpty();
    }

    // ── Delete / reset (by id) ─────────────────────────────────────────────

    [TestMethod]
    public async Task Delete_RemovesRowAndConstraints_AndWritesADeletedAuditEntry()
    {
        AsTenant(TenantA);
        var created = await CreateAsync(
            NotificationTemplateKind.Reminder, NotificationChannel.Email,
            constraints: [(TemplateConstraintDimension.Stream, "Science")]);

        await _delete.HandleAsync(new DeleteTenantNotificationTemplate(created.Id, "reset"));

        (await GetDefaultAsync(NotificationTemplateKind.Reminder, NotificationChannel.Email)).Should().BeNull();
        (await _db.TemplateConstraints.AsNoTracking().CountAsync()).Should().Be(0);

        var audit = await _audit.HandleAsync(
            new GetTemplateAuditEntries(NotificationTemplateKind.Reminder, NotificationChannel.Email));

        audit.Should().HaveCount(2);
        var deleted = audit.Single(a => a.ChangeKind == TemplateChangeKind.Deleted);
        deleted.NewBody.Should().BeNull();
        deleted.PreviousBody.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Delete_OneCandidate_LeavesTheOtherRowsLive()
    {
        // By-id identity matters here: the retired (kind, channel)-keyed reset could not say which
        // candidate it meant.
        AsTenant(TenantA);
        var unconstrained = await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);
        var constrained = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            constraints: [(TemplateConstraintDimension.GradeLevel, "Grade 3")]);

        await _delete.HandleAsync(new DeleteTenantNotificationTemplate(constrained.Id, "drop the grade one"));

        var rows = await _getAll.HandleAsync(new GetTenantNotificationTemplates());
        rows.Should().ContainSingle().Which.Id.Should().Be(unconstrained.Id);
    }

    [TestMethod]
    public async Task AuditTrail_SurvivesReset()
    {
        // Regression: an id-based audit join lost the whole trail once the template
        // row was soft-deleted, so a reset wiped the template's auditing history
        // from the UI (spec §3.9 requires it to be visible).
        AsTenant(TenantA);
        var created = await CreateAsync(NotificationTemplateKind.Completion, NotificationChannel.SMS);

        await _delete.HandleAsync(new DeleteTenantNotificationTemplate(created.Id, "reset"));

        var audit = await _audit.HandleAsync(
            new GetTemplateAuditEntries(NotificationTemplateKind.Completion, NotificationChannel.SMS));

        audit.Should().HaveCount(2);
        audit.Select(a => a.ChangeKind).Should().Contain(TemplateChangeKind.Created);
        audit.Select(a => a.ChangeKind).Should().Contain(TemplateChangeKind.Deleted);
    }

    [TestMethod]
    public async Task Delete_UnknownId_IsNotFound_AndWritesNoAuditRow()
    {
        AsTenant(TenantA);

        var act = () => _delete.HandleAsync(new DeleteTenantNotificationTemplate(Guid.NewGuid(), "reset"));

        await act.Should().ThrowAsync<TenantNotificationTemplateNotFoundException>();
        (await _audit.HandleAsync(
            new GetTemplateAuditEntries(NotificationTemplateKind.Publish, NotificationChannel.SMS)))
            .Should().BeEmpty();
    }

    [TestMethod]
    public async Task Delete_ThenCreate_RecreatesTheSlot()
    {
        // The (tenant, kind, channel) index is filtered on is_deleted = false, so a reset must not
        // block re-creating the same slot.
        AsTenant(TenantA);
        var created = await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email, body: "First");

        await _delete.HandleAsync(new DeleteTenantNotificationTemplate(created.Id, "reset"));

        var recreated = await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email, body: "Second");

        recreated.Body.Should().Be("Second");
        (await _db.TenantNotificationTemplates.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task AuditTrail_IsScopedToTheCurrentTenant()
    {
        AsTenant(TenantA);
        await CreateAsync(NotificationTemplateKind.Publish, NotificationChannel.Email);

        AsTenant(TenantB);
        var tenantBAudit = await _audit.HandleAsync(
            new GetTemplateAuditEntries(NotificationTemplateKind.Publish, NotificationChannel.Email));

        tenantBAudit.Should().BeEmpty();
    }
}
