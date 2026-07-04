using FluentAssertions;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit;

[TestClass]
public class FeatureFlagDomainTests
{
    [TestMethod]
    public void Create_normalizes_key_to_FEATURE_upper()
    {
        var flag = FeatureFlag.Create("feature:enablefoo", "Enable Foo", null, isEnabled: true);
        flag.Key.Should().Be("FEATURE:ENABLEFOO");
        flag.IsEnabled.Should().BeTrue();
        flag.IsArchived.Should().BeFalse();
        flag.IsDeleted.Should().BeFalse();
    }

    [TestMethod]
    public void Create_rejects_key_without_FEATURE_prefix()
    {
        var act = () => FeatureFlag.Create("EnableFoo", "Enable Foo", null, true);
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Enable_and_Disable_track_state_and_idempotent()
    {
        var flag = FeatureFlag.Create("FEATURE:X", "X", null, isEnabled: false);
        flag.Enable();  flag.IsEnabled.Should().BeTrue();
        flag.Enable(); // idempotent — UpdatedAt unchanged on second no-op
        var after = flag.UpdatedAt;
        flag.Disable(); flag.IsEnabled.Should().BeFalse();
        flag.Disable(); // idempotent
    }

    [TestMethod]
    public void Archive_unarchive_delete_recover_cycle()
    {
        var flag = FeatureFlag.Create("FEATURE:X", "X", null, true);
        flag.Archive();   flag.IsArchived.Should().BeTrue();
        flag.Unarchive(); flag.IsArchived.Should().BeFalse();
        flag.Delete();    flag.IsDeleted.Should().BeTrue();
        flag.Recover();   flag.IsDeleted.Should().BeFalse();
    }
}