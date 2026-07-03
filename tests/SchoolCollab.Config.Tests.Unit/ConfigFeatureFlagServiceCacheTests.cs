using FluentAssertions;
using SchoolCollab.Config.Core.Caching;

namespace SchoolCollab.Config.Tests.Unit;

/// <summary>
/// Regression tests for the case-insensitivity bug in
/// <see cref="ConfigFeatureFlagService"/>. The Config API / DB stores flag keys
/// in the canonical upper-case form produced by
/// <c>FeatureFlag.NormalizeKey</c> (e.g. <c>FEATURE:ENABLECODEDVALUESAICHAT</c>),
/// but call sites look up with the humanised mixed-case form (e.g.
/// <c>FEATURE:EnableCodedValuesAiChat</c>). The factory builds the cache
/// dictionary with <see cref="StringComparer.OrdinalIgnoreCase"/>, but
/// HybridCache serializes the dictionary through its L2/L1 serializer on a
/// round-trip, and the deserialized copy is rebuilt with the DEFAULT
/// (case-sensitive) comparer — silently dropping the case-insensitive comparer.
/// <see cref="ConfigFeatureFlagService.WithCaseInsensitiveLookup"/> re-wraps the
/// dictionary on every read so the lookup is case-insensitive regardless of how
/// the cache returned it. These tests pin that behaviour without standing up
/// HybridCache/HttpClient.
/// </summary>
[TestClass]
public class ConfigFeatureFlagServiceCacheTests
{
    [TestMethod]
    public void Mixed_Case_Lookup_Matches_Canonical_Upper_Case_Key_After_Re_Wrap()
    {
        // Arrange: simulate the dictionary HybridCache returns AFTER an L2/L1
        // serialization round-trip — a plain Dictionary with the DEFAULT
        // (case-sensitive) comparer, holding the canonical upper-case key the
        // DB stores. A mixed-case TryGetValue against THIS dict misses.
        var deserializedByCache = new Dictionary<string, ResolvedFlag>(StringComparer.Ordinal)
        {
            ["FEATURE:ENABLECODEDVALUESAICHAT"] =
                new("FEATURE:ENABLECODEDVALUESAICHAT", IsEnabled: true, ResolvedFlagSource.GlobalDefault, DateTimeOffset.UtcNow),
        };

        // Pre-fix behaviour: the raw deserialized dict is case-sensitive, so the
        // mixed-case caller key misses and the flag evaluates as disabled. This
        // is the exact "AI chat won't render" symptom.
        deserializedByCache.TryGetValue("FEATURE:EnableCodedValuesAiChat", out _)
            .Should().BeFalse("the deserialized dict lost the OrdinalIgnoreCase comparer");

        // Act: re-wrap the way ResolveAllAsync now does on every read.
        var resolved = ConfigFeatureFlagService.WithCaseInsensitiveLookup(deserializedByCache);

        // Assert: the mixed-case caller key now matches the canonical upper-case
        // stored key and the flag resolves enabled.
        resolved.TryGetValue("FEATURE:EnableCodedValuesAiChat", out var flag).Should().BeTrue();
        flag.IsEnabled.Should().BeTrue();
    }

    [TestMethod]
    public void Re_Wrap_Is_Idempotent_And_Preserves_Entries()
    {
        // The factory-built dict already has OrdinalIgnoreCase; re-wrapping it
        // (the L1-hit path) must not lose entries or change values.
        var original = new Dictionary<string, ResolvedFlag>(StringComparer.OrdinalIgnoreCase)
        {
            ["FEATURE:ENABLECODEDVALUESAICHAT"] =
                new("FEATURE:ENABLECODEDVALUESAICHAT", true, ResolvedFlagSource.GlobalDefault, DateTimeOffset.UtcNow),
            ["FEATURE:DISABLEOIDCAUTH"] =
                new("FEATURE:DISABLEOIDCAUTH", false, ResolvedFlagSource.GlobalDefault, DateTimeOffset.UtcNow),
        };

        var rewrapped = ConfigFeatureFlagService.WithCaseInsensitiveLookup(original);

        rewrapped.Should().HaveCount(2);
        rewrapped["FEATURE:EnableCodedValuesAiChat"].IsEnabled.Should().BeTrue();
        rewrapped["FEATURE:DisableOIDCAuth"].IsEnabled.Should().BeFalse();
    }

    [TestMethod]
    public void Re_Wrap_Honours_Mixed_Case_Config_Fallback_Keys_Too()
    {
        // The IConfiguration fallback stores keys as-authored in appsettings
        // (mixed-case), so the re-wrap must be case-insensitive in BOTH
        // directions: a canonical upper-case caller key must also match a
        // mixed-case stored key.
        var fallback = new Dictionary<string, ResolvedFlag>(StringComparer.Ordinal)
        {
            ["FEATURE:EnableCodedValuesAiChat"] =
                new("FEATURE:EnableCodedValuesAiChat", true, ResolvedFlagSource.ConfigurationFallback, DateTimeOffset.UtcNow),
        };

        var rewrapped = ConfigFeatureFlagService.WithCaseInsensitiveLookup(fallback);

        rewrapped.TryGetValue("FEATURE:ENABLECODEDVALUESAICHAT", out var flag).Should().BeTrue();
        flag.IsEnabled.Should().BeTrue();
    }
}