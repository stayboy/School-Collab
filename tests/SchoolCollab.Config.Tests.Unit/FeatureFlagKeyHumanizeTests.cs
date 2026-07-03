using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Config.Core.Domain;

namespace SchoolCollab.Config.Tests.Unit;

[TestClass]
public class FeatureFlagKeyHumanizeTests
{
    [TestMethod]
    public void ToTitleCase_SplitsColonAndTitleizesParts()
    {
        var key = "FEATURE:ENABLECODEDVALUESAICHAT";
        var result = FeatureFlagKeyDisplay.ToTitleCase(key);
        Assert.AreEqual("Feature: Enable Coded Values Ai Chat", result);
    }

    [TestMethod]
    public void ToTitleCase_HandlesKeyWithoutColon()
    {
        var key = "SomeSimpleFlag";
        var result = FeatureFlagKeyDisplay.ToTitleCase(key);
        Assert.AreEqual("Some Simple Flag", result);
    }

    [TestMethod]
    public void ToTitleCase_ReturnsEmpty_ForWhitespace()
    {
        Assert.AreEqual("", FeatureFlagKeyDisplay.ToTitleCase(""));
    }

    [TestMethod]
    public void ToTitleCase_SplitsOnMultiSegmentColons()
    {
        var key = "BETA:ROLLOUT:NEWDASHBOARD";
        var result = FeatureFlagKeyDisplay.ToTitleCase(key);
        Assert.AreEqual("Beta: Rollout: New Dashboard", result);
    }

    [TestMethod]
    public void ToPascalCase_PreservesPrefixAndPascalCasesArea()
    {
        var key = "FEATURE:ENABLECODEDVALUESAICHAT";
        var result = FeatureFlagKeyDisplay.ToPascalCase(key);
        Assert.AreEqual("FEATURE:EnableCodedValuesAiChat", result);
    }

    [TestMethod]
    public void ToPascalCase_HandlesKeyWithoutColon()
    {
        var key = "SomeSimpleFlag";
        var result = FeatureFlagKeyDisplay.ToPascalCase(key);
        Assert.AreEqual("SomeSimpleFlag", result);
    }

    [TestMethod]
    public void ToPascalCase_ReturnsEmpty_ForWhitespace()
    {
        Assert.AreEqual("", FeatureFlagKeyDisplay.ToPascalCase(""));
    }

    [TestMethod]
    public void ToPascalCase_SplitsOnMultiSegmentColons()
    {
        var key = "BETA:ROLLOUT:NEWDASHBOARD";
        var result = FeatureFlagKeyDisplay.ToPascalCase(key);
        Assert.AreEqual("BETA:Rollout:NewDashboard", result);
    }
}
