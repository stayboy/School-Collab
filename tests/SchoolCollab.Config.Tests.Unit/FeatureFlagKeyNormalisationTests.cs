using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Config.Core.Data;
using SchoolCollab.Config.Core.Domain;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Config.Tests.Unit;

/// <summary>
/// Regression tests for the migrator's <c>SeedEnableCodedValuesAiChatAsync</c>
/// logic. The seed must compare against the canonical normalised key
/// (<see cref="FeatureFlag.NormalizeKey"/>) — not the mixed-case key from the
/// source code — otherwise a re-run against a database that already contains
/// the flag violates the partial unique index <c>ix_feature_flags_key_unique</c>
/// with "duplicate key value violates unique constraint".
/// </summary>
[TestClass]
public class FeatureFlagKeyNormalisationTests
{
    private static DbContextOptions<ConfigDbContext> InMemoryOptions() =>
        new DbContextOptionsBuilder<ConfigDbContext>()
            .UseInMemoryDatabase($"cfg-flag-seed-{Guid.NewGuid()}")
            .Options;

    [TestMethod]
    public void NormalizeKey_Uppercases_Area_After_Feature_Prefix()
    {
        // Arrange + Act
        var normalized = FeatureFlag.NormalizeKey("FEATURE:EnableCodedValuesAiChat");

        // Assert
        normalized.Should().Be("FEATURE:ENABLECODEDVALUESAICHAT",
            "FeatureFlag.Create persists the upper-cased area; the seeder must " +
            "query against this canonical form or the existence check will miss " +
            "a row from a previous successful run");
    }

    [TestMethod]
    public async Task Existence_Check_With_Canonical_Key_Finds_Previously_Seeded_Flag()
    {
        // Arrange: simulate a previous successful seed (e.g. FEATURE:ENABLECODEDVALUESAICHAT
        // was inserted by FeatureFlag.Create on an earlier run).
        var options = InMemoryOptions();
        await using (var seed = new ConfigDbContext(options, new TenantProvider()))
        {
            seed.FeatureFlags.Add(
                FeatureFlag.Create("FEATURE:EnableCodedValuesAiChat", "desc", null, isEnabled: true));
            await seed.SaveChangesAsync();
        }

        // Act: check using the canonical normalised key (the FIX), not the mixed-case source key.
        await using var db = new ConfigDbContext(options, new TenantProvider());
        var canonicalKey = FeatureFlag.NormalizeKey("FEATURE:EnableCodedValuesAiChat");
        var existsWithCanonical = await db.FeatureFlags.AnyAsync(f => f.Key == canonicalKey);

        // Assert
        existsWithCanonical.Should().BeTrue(
            "the seeder must look up the flag by its canonical normalised key");
    }

    [TestMethod]
    public async Task Existence_Check_With_Mixed_Case_Key_Misses_Normalised_Storage()
    {
        // Arrange: same as above — a previous run stored the canonical form.
        var options = InMemoryOptions();
        await using (var seed = new ConfigDbContext(options, new TenantProvider()))
        {
            seed.FeatureFlags.Add(
                FeatureFlag.Create("FEATURE:EnableCodedValuesAiChat", "desc", null, isEnabled: true));
            await seed.SaveChangesAsync();
        }

        // Act: replicate the original buggy check using the mixed-case key.
        await using var db = new ConfigDbContext(options, new TenantProvider());
        var existsWithMixedCase = await db.FeatureFlags
            .AnyAsync(f => f.Key == "FEATURE:EnableCodedValuesAiChat");

        // Assert: documents the bug. The original seeder used the mixed-case
        // literal which (a) would miss the row in InMemory's case-insensitive
        // comparison and (b) definitely misses it in Postgres (case-sensitive
        // text comparison), so the re-run would try to insert a duplicate and
        // violate ix_feature_flags_key_unique.
        existsWithMixedCase.Should().BeFalse(
            "this is the pre-fix behaviour that causes the unique-index violation " +
            "on a re-run; the seeder must use FeatureFlag.NormalizeKey instead");
    }
}
