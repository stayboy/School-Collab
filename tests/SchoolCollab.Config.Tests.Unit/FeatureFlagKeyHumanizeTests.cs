using Humanizer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Config.Tests.Unit;

[TestClass]
public class FeatureFlagKeyHumanizeTests
{
    [TestMethod]
    public void HumanizeKey_SplitsColonAndTitleizesParts()
    {
        var key = "FEATURE:ENABLECODEDVALUESAICHAT";
        var result = HumanizeKey(key);
        Assert.AreEqual("Feature: Enable Coded Values Ai Chat", result);
    }

    [TestMethod]
    public void HumanizeKey_HandlesKeyWithoutColon()
    {
        var key = "SomeSimpleFlag";
        var result = HumanizeKey(key);
        Assert.AreEqual("Some Simple Flag", result);
    }

    [TestMethod]
    public void HumanizeKey_ReturnsEmpty_ForWhitespace()
    {
        Assert.AreEqual("", HumanizeKey(""));
    }

    private static string HumanizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return key;

        var parts = key.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return key;

        return string.Join(": ", parts.Select(p =>
        {
            var humanized = p.Humanize();
            if (humanized == humanized.ToUpperInvariant())
                return humanized.ToLowerInvariant().Titleize();
            return humanized.Titleize();
        }));
    }
}
