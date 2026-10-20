using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.CreateTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetApplicableNotificationTemplates;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// The grade-detail applicability read (spec §3.11, round notification-templates-b3a): which of a
/// tenant's templates <b>can apply</b> to one grade, grouped into the eight authorable kind × channel
/// slots. Every fixture is built through the real create handler, so constraints carry the store's own
/// trim-at-write shape rather than a hand-shaped string the query could not meet in production.
/// </summary>
[TestClass]
public class ApplicableNotificationTemplatesHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // Fixed grade ids: the casing fixture must differ from the queried id only in case, and a
    // generated guid would make that assertion probabilistic.
    private static readonly Guid GradeA = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");
    private static readonly Guid GradeB = Guid.Parse("abcdef02-2345-6789-abcd-ef0123456789");
    private static readonly Guid GradeC = Guid.Parse("abcdef03-2345-6789-abcd-ef0123456789");

    private SettingsDbContext _db = default!;
    private TenantProvider _tenants = default!;
    private CreateTenantNotificationTemplateHandler _create = default!;
    private GetApplicableNotificationTemplatesHandler _applicable = default!;

    [TestInitialize]
    public void Setup()
    {
        _tenants = new TenantProvider();
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase($"ApplicableNotificationTemplates_{Guid.NewGuid()}")
            .Options;

        _db = new SettingsDbContext(options, _tenants);
        var repository = new CodedValueRepository(_db);
        var auditor = new TemplateAuditRecorder(new SystemActorAccessor("test:actor", "Test Actor"));

        _create = new CreateTenantNotificationTemplateHandler(_db, _tenants, auditor);
        _applicable = new GetApplicableNotificationTemplatesHandler(
            _db, repository, new CodedValueResolver(repository), _tenants);
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    private void AsTenant(Guid tenantId) =>
        _tenants.SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));

    private Task<TenantNotificationTemplateDto> CreateAsync(
        NotificationTemplateKind kind,
        NotificationChannel channel,
        params (TemplateConstraintDimension Dimension, string Value)[] constraints) =>
        _create.HandleAsync(new CreateTenantNotificationTemplate(
            new CreateTenantNotificationTemplateRequest(
                kind, channel, $"Subject for {kind}", "Body for {{Title}}",
                constraints.Select(c => new TemplateConstraintRequest(c.Dimension, c.Value)).ToList(),
                "test"),
            "test"));

    /// <summary>Seeds a shared-blueprint coded value — the grade / stream rows the display resolves against.</summary>
    private async Task<CodedValue> SeedCodedValueAsync(string code, string name)
    {
        var codedValue = CodedValue.Create(code, name, null, parentId: null, displayOrder: 0);
        _db.CodedValues.Add(codedValue);
        await _db.SaveChangesAsync();
        return codedValue;
    }

    private Task<ApplicableNotificationTemplatesDto> ApplicableAsync(Guid gradeLevelId) =>
        _applicable.HandleAsync(new GetApplicableNotificationTemplates(gradeLevelId));

    private static ApplicableTemplateSlotDto Slot(
        ApplicableNotificationTemplatesDto result,
        NotificationTemplateKind kind,
        NotificationChannel channel) =>
        result.Slots.Single(s => s.Kind == kind && s.Channel == channel);

    // ── Applicability ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task ReturnsTheTemplateConstrainedToTheQueriedGrade()
    {
        AsTenant(TenantA);
        var grade = await SeedCodedValueAsync("GRADE_3", "Grade 3");
        var created = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            (TemplateConstraintDimension.GradeLevel, grade.Id.ToString("D")));

        var result = await ApplicableAsync(grade.Id);

        var template = Slot(result, NotificationTemplateKind.Publish, NotificationChannel.Email)
            .Templates.Should().ContainSingle().Subject;
        template.Id.Should().Be(created.Id);
        template.IsDefault.Should().BeFalse("a constrained candidate is never the tenant default");
    }

    [TestMethod]
    public async Task MatchesTheGradeIdRegardlessOfCase()
    {
        // The plain-Ordinal kill: the store trims at write and preserves casing, while
        // Guid.ToString("D") is lowercase — so an uppercase id must still be found, or a template that
        // fires at publish time would vanish from this card.
        var uppercase = GradeA.ToString("D").ToUpperInvariant();
        uppercase.Should().NotBe(GradeA.ToString("D"), "the fixture must actually differ in casing");

        AsTenant(TenantA);
        var created = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            (TemplateConstraintDimension.GradeLevel, uppercase));

        var result = await ApplicableAsync(GradeA);

        var template = Slot(result, NotificationTemplateKind.Publish, NotificationChannel.Email)
            .Templates.Should().ContainSingle().Subject;
        template.Id.Should().Be(created.Id);
        template.IsDefault.Should().BeFalse();
    }

    [TestMethod]
    public async Task ExcludesATemplateConstrainedToAnotherGrade()
    {
        AsTenant(TenantA);
        await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            (TemplateConstraintDimension.GradeLevel, GradeB.ToString("D")));

        var result = await ApplicableAsync(GradeA);

        Slot(result, NotificationTemplateKind.Publish, NotificationChannel.Email)
            .Templates.Should().BeEmpty("the grade dimension is the one dimension that filters");
    }

    [TestMethod]
    public async Task KeepsATemplateConstrainedOnAnotherDimension()
    {
        // §3.11: other dimensions are DISPLAYED, never filtered — a stream-constrained template can
        // still apply to this grade, because the card is an inventory, not an evaluation.
        var streamId = Guid.Parse("c0ffee00-1111-2222-3333-444444444444");

        AsTenant(TenantA);
        var created = await CreateAsync(
            NotificationTemplateKind.Reminder, NotificationChannel.SMS,
            (TemplateConstraintDimension.Stream, streamId.ToString("D")));

        var result = await ApplicableAsync(GradeA);

        var template = Slot(result, NotificationTemplateKind.Reminder, NotificationChannel.SMS)
            .Templates.Should().ContainSingle().Subject;
        template.Id.Should().Be(created.Id);
        template.Constraints.Should().ContainSingle()
            .Which.Dimension.Should().Be(TemplateConstraintDimension.Stream);
    }

    [TestMethod]
    public async Task FlagsEachSlotsOwnDefault_AndDoesNotRepeatItAcrossSlots()
    {
        // One default belongs to one kind × channel: the unconstrained row is not repeated into the
        // other seven slots.
        AsTenant(TenantA);
        var publishDefault = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email);
        var reminderDefault = await CreateAsync(
            NotificationTemplateKind.Reminder, NotificationChannel.SMS);

        var result = await ApplicableAsync(GradeA);

        var publish = Slot(result, NotificationTemplateKind.Publish, NotificationChannel.Email)
            .Templates.Should().ContainSingle().Subject;
        publish.Id.Should().Be(publishDefault.Id);
        publish.IsDefault.Should().BeTrue("the row with no constraints is the tenant default");

        var reminder = Slot(result, NotificationTemplateKind.Reminder, NotificationChannel.SMS)
            .Templates.Should().ContainSingle().Subject;
        reminder.Id.Should().Be(reminderDefault.Id);
        reminder.IsDefault.Should().BeTrue();

        result.Slots.Where(s => s.Templates.Count > 0).Should().HaveCount(2);
        result.Slots.SelectMany(s => s.Templates).Count(t => t.Id == publishDefault.Id).Should().Be(1);
    }

    [TestMethod]
    public async Task ReturnsTheEightEmailAndSmsSlotsOnly()
    {
        // Slot shape, not row shape: every authorable kind × channel is present (empty or not) so the
        // card renders uniformly, and WhatsApp has no slot — the entity refuses to create such a row.
        AsTenant(TenantA);

        var result = await ApplicableAsync(GradeA);

        result.Slots.Should().HaveCount(8);
        result.Slots.Select(s => (s.Kind, s.Channel)).Should().OnlyHaveUniqueItems();
        result.Slots.Select(s => s.Channel).Should()
            .OnlyContain(c => c == NotificationChannel.Email || c == NotificationChannel.SMS);
        result.Slots.Should().NotContain(s => s.Channel == NotificationChannel.WhatsApp);
        result.Slots.Select(s => (s.Kind, s.Channel)).Should().BeEquivalentTo(
            from kind in new[]
            {
                NotificationTemplateKind.Publish, NotificationTemplateKind.Reminder,
                NotificationTemplateKind.Completion, NotificationTemplateKind.Overdue,
            }
            from channel in new[] { NotificationChannel.Email, NotificationChannel.SMS }
            select (kind, channel));
    }

    // ── Display resolution ──────────────────────────────────────────────────

    [TestMethod]
    public async Task ResolvesCodeBackedConstraintDisplayThroughTheCodedValueResolver()
    {
        var grade = await SeedCodedValueAsync("GRADE_3", "Grade 3");
        var stream = await SeedCodedValueAsync("GRSTREAMS_SCI", "Science");

        AsTenant(TenantA);
        await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            (TemplateConstraintDimension.GradeLevel, grade.Id.ToString("D")),
            (TemplateConstraintDimension.Stream, stream.Id.ToString("D")));

        var constraints = Slot(await ApplicableAsync(grade.Id),
            NotificationTemplateKind.Publish, NotificationChannel.Email).Templates.Single().Constraints;

        constraints.Single(c => c.Dimension == TemplateConstraintDimension.GradeLevel)
            .Display.Should().Be("Grade 3");
        constraints.Single(c => c.Dimension == TemplateConstraintDimension.Stream)
            .Display.Should().Be("Science");
    }

    [TestMethod]
    public async Task FallsBackToTheRawValueWhenACodeBackedConstraintDoesNotResolve()
    {
        var unknownId = Guid.Parse("deadbeef-0000-0000-0000-000000000000");

        AsTenant(TenantA);
        await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            (TemplateConstraintDimension.Stream, unknownId.ToString("D")),
            (TemplateConstraintDimension.ActivityGroup, "After-school club"),
            (TemplateConstraintDimension.AssignmentType, "Digital"));

        var constraints = Slot(await ApplicableAsync(GradeA),
            NotificationTemplateKind.Publish, NotificationChannel.Email).Templates.Single().Constraints;

        constraints.Single(c => c.Dimension == TemplateConstraintDimension.AssignmentType)
            .Display.Should().Be("Digital", "the assignment-type dimension stores the enum name itself");
        constraints.Single(c => c.Dimension == TemplateConstraintDimension.Stream)
            .Display.Should().Be(unknownId.ToString("D"),
                "an id with no coded value falls back to the stored value rather than blanking the card");
        constraints.Single(c => c.Dimension == TemplateConstraintDimension.ActivityGroup)
            .Display.Should().Be("After-school club", "a value that is not an id displays as stored");
    }

    // ── Order ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RanksByDistinctDimensionCount_NotByConstraintRowCount()
    {
        // Discriminating fixture: the three-value candidate has MORE constraint ROWS but fewer distinct
        // DIMENSIONS (three GradeLevel values on one candidate are one dimension, §3.1.1), and it is
        // created FIRST — so a row-count sort and a CreatedAt-first sort both invert this order.
        AsTenant(TenantA);
        var threeValues = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            (TemplateConstraintDimension.GradeLevel, GradeA.ToString("D")),
            (TemplateConstraintDimension.GradeLevel, GradeB.ToString("D")),
            (TemplateConstraintDimension.GradeLevel, GradeC.ToString("D")));
        var twoDimensions = await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            (TemplateConstraintDimension.GradeLevel, GradeA.ToString("D")),
            (TemplateConstraintDimension.Stream, Guid.Parse("c0ffee00-1111-2222-3333-444444444444").ToString("D")));

        var templates = Slot(await ApplicableAsync(GradeA),
            NotificationTemplateKind.Publish, NotificationChannel.Email).Templates;

        templates.Select(t => t.Id).Should().Equal(
            [twoDimensions.Id, threeValues.Id],
            "specificity is the count of distinct constrained dimensions (spec §3.1.1), and the "
            + "three-value candidate was created first, so neither a row-count nor a CreatedAt-first "
            + "sort can produce this order");
    }

    // ── Tenant isolation ────────────────────────────────────────────────────

    [TestMethod]
    public async Task NeverReturnsAnotherTenantsTemplates()
    {
        AsTenant(TenantB);
        await CreateAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            (TemplateConstraintDimension.GradeLevel, GradeA.ToString("D")));

        AsTenant(TenantA);
        var result = await ApplicableAsync(GradeA);

        result.Slots.SelectMany(s => s.Templates).Should().BeEmpty(
            "the tenant query filter scopes the read; another tenant's candidate is not this tenant's");
    }
}
