using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.MigrationService.Seeding;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// The notification-template handlers mint a template code through the real
/// <see cref="IEntityCodeGenerator"/>, so a handler test needs the production
/// <c>NOTIFICATION_TEMPLATE_CODE</c> rule in its own store. Seeding it with the real
/// <see cref="EntityCodeRuleSeeder"/> keeps the rule's shape (the <c>TPL</c> stamp plus a numeric
/// sequence) stated once, in the place that ships it, instead of retyped per test class.
/// </summary>
internal static class TestEntityCodeSetup
{
    /// <summary>
    /// Seeds the shipped entity-code rules into <paramref name="options"/>' in-memory store and returns
    /// a generator reading that same store — the store is keyed by database name, so the seeding context
    /// and every generator context share it.
    /// </summary>
    public static async Task<IEntityCodeGenerator> SeedAndCreateGeneratorAsync(
        DbContextOptions<SettingsDbContext> options, TenantProvider tenantProvider)
    {
        await using (var db = new SettingsDbContext(options, tenantProvider))
        {
            await new EntityCodeRuleSeeder(
                    db,
                    new TenantContextAccessor(tenantProvider),
                    NullLogger<EntityCodeRuleSeeder>.Instance)
                .SeedAsync();
        }

        return new EntityCodeGenerator(
            new TestSettingsDbContextFactory(options, tenantProvider),
            tenantProvider,
            NullLogger<EntityCodeGenerator>.Instance);
    }
}
