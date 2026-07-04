using FluentAssertions;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit;

[TestClass]
public class TenantFeatureFlagOverrideTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Flag = Guid.NewGuid();

    [TestMethod]
    public void Create_null_enabled_means_inherit()
    {
        var ov = TenantFeatureFlagOverride.Create(Tenant, Flag, isEnabled: null, "pilot", null, null);
        ov.IsEnabled.Should().BeNull();
        ov.Reason.Should().Be("pilot");
    }

    [TestMethod]
    public void Create_requires_reason()
    {
        var act = () => TenantFeatureFlagOverride.Create(Tenant, Flag, false, "  ", null, null);
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Create_rejects_window_where_to_precedes_from()
    {
        var from = DateTimeOffset.UtcNow;
        var to = from.AddHours(-1);
        var act = () => TenantFeatureFlagOverride.Create(Tenant, Flag, true, "r", from, to);
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void IsInEffectAt_respects_effective_window()
    {
        var now = DateTimeOffset.UtcNow;
        var future = now.AddHours(1);
        var ov = TenantFeatureFlagOverride.Create(Tenant, Flag, true, "r", future, null);
        ov.IsInEffectAt(now).Should().BeFalse();
        ov.IsInEffectAt(future.AddMinutes(1)).Should().BeTrue();
    }

    [TestMethod]
    public void Update_changes_enabled_and_reason()
    {
        var ov = TenantFeatureFlagOverride.Create(Tenant, Flag, null, "first", null, null);
        ov.Update(isEnabled: true, "second", null, null);
        ov.IsEnabled.Should().BeTrue();
        ov.Reason.Should().Be("second");
    }
}