using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.MigrationService.Seeding;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Naming spec D5/D7 — the <c>NOTIFICATION_TEMPLATE_CODE</c> rule the create/adopt/promote handlers mint
/// template codes from. Exercised through the <b>real</b> <see cref="EntityCodeRuleSeeder"/> and the real
/// <see cref="EntityCodeGenerator"/>, so the shape asserted here is the shape that ships: a fixed
/// <c>TPL</c> stamp followed by a two-digit numeric sequence, giving <c>TPL01</c>, <c>TPL02</c>, … and
/// reaching <c>TPL100</c> at the hundredth template.
/// </summary>
[TestClass]
public class NotificationTemplateCodeRuleTests
{
    private static DbContextOptions<SettingsDbContext> NewOptions() =>
        new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase($"NotificationTemplateCodeRule_{Guid.NewGuid()}")
            .Options;

    private static EntityCodeRuleSeeder NewSeeder(SettingsDbContext db, TenantProvider tenants) =>
        new(db, new TenantContextAccessor(tenants), NullLogger<EntityCodeRuleSeeder>.Instance);

    [TestMethod]
    public async Task Seeder_CreatesTheTemplateRule_WithAStampAndANumericSequence()
    {
        var options = NewOptions();
        var tenants = new TenantProvider();

        await using (var seedDb = new SettingsDbContext(options, tenants))
        {
            await NewSeeder(seedDb, tenants).SeedAsync();
        }

        await using var db = new SettingsDbContext(options, tenants);
        var rule = await db.EntityCodeRules
            .Include(x => x.Segments)
            .SingleAsync(x => x.Code == "NOTIFICATION_TEMPLATE_CODE");

        rule.Name.Should().Be("Notification Template Code Template");
        rule.IsActive.Should().BeTrue();
        rule.TenantId.Should().BeNull("the rule is a shared blueprint, like student and topic codes");

        var segments = rule.Segments.OrderBy(s => s.Index).ToList();
        segments.Should().HaveCount(2);
        segments[0].Type.Should().Be(SegmentType.Fixed);
        segments[0].FixedText.Should().Be("TPL");
        segments[1].Type.Should().Be(SegmentType.NumericSequence);
        segments[1].MinWidth.Should().Be(2);
        segments[1].ResetPeriod.Should().Be(ResetPeriod.None, "a template code never renumbers on a period");
    }

    [TestMethod]
    public async Task Seeder_IsIdempotent_SoASecondRunDoesNotForkTheRule()
    {
        // The ordering constraint in the spec: the MigrationService may run many times, and a second
        // rule with the same Code would make the generator's single-row read ambiguous.
        var options = NewOptions();
        var tenants = new TenantProvider();

        await using (var firstRun = new SettingsDbContext(options, tenants))
        {
            await NewSeeder(firstRun, tenants).SeedAsync();
        }

        await using (var secondRun = new SettingsDbContext(options, tenants))
        {
            await NewSeeder(secondRun, tenants).SeedAsync();
        }

        await using var db = new SettingsDbContext(options, tenants);
        (await db.EntityCodeRules.CountAsync(x => x.Code == "NOTIFICATION_TEMPLATE_CODE"))
            .Should().Be(1);
    }

    [TestMethod]
    public async Task Generator_ProducesPaddedCodes_AndReachesTheHundredth()
    {
        var options = NewOptions();
        var tenants = new TenantProvider();

        await using (var seedDb = new SettingsDbContext(options, tenants))
        {
            await NewSeeder(seedDb, tenants).SeedAsync();
        }

        var generator = new EntityCodeGenerator(
            new TestSettingsDbContextFactory(options, tenants), tenants,
            NullLogger<EntityCodeGenerator>.Instance);

        (await generator.GenerateAsync("NOTIFICATION_TEMPLATE_CODE")).Should().Be("TPL01");
        (await generator.GenerateAsync("NOTIFICATION_TEMPLATE_CODE")).Should().Be("TPL02");

        // The hundredth row is the case a two-digit width alone would collide on (a numeric segment
        // without an explicit upper limit caps at 10^MinWidth - 1 = 99).
        string last = "TPL02";
        for (var i = 3; i <= 100; i++)
            last = await generator.GenerateAsync("NOTIFICATION_TEMPLATE_CODE");

        last.Should().Be("TPL100", "the width pads, the limit lets the sequence grow past 99");
    }
}
