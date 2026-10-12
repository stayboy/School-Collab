using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Tests.Unit;

/// <summary>
/// Covers <see cref="MobileApiOptions"/> — the mobile head's config-only base-address rule
/// (spec D8). Addresses are built with <see cref="UriBuilder"/> rather than typed as literals
/// on purpose: the rule the class enforces is "never a URL string literal in C#", and a test
/// that violated it would be the one place the rule is allowed to rot.
/// </summary>
[TestClass]
public class MobileApiOptionsTests
{
    [TestMethod]
    public void FromConfiguration_BindsEveryConfiguredApi_FromTheMobileApiSection()
    {
        var options = MobileApiOptions.FromConfiguration(BuildConfiguration(
            ("MobileApi:StudentsApi", Root("students.example", 7450)),
            ("MobileApi:SettingsApi", Root("settings.example", 7460)),
            ("MobileApi:AssignmentsApi", Root("assignments.example", 7199))));

        options.Resolve(nameof(MobileApiOptions.StudentsApi)).Should().Be(new Uri(Root("students.example", 7450)));
        options.Resolve(nameof(MobileApiOptions.SettingsApi)).Should().Be(new Uri(Root("settings.example", 7460)));
        options.Resolve(nameof(MobileApiOptions.AssignmentsApi)).Should().Be(new Uri(Root("assignments.example", 7199)));
        options.MissingBaseAddresses().Should().BeEmpty();
    }

    [TestMethod]
    public void Resolve_AppendsTheTrailingSlashToAPathSuffix()
    {
        var options = MobileApiOptions.FromConfiguration(BuildConfiguration(
            ("MobileApi:AssignmentsApi", WithPath("assignments.example", 7199, "api"))));

        options.Resolve(nameof(MobileApiOptions.AssignmentsApi))
            .Should().Be(new Uri($"{WithPath("assignments.example", 7199, "api")}/"));
    }

    [TestMethod]
    public void Resolve_ReturnsNull_ForAnUnconfiguredApi()
    {
        var options = MobileApiOptions.FromConfiguration(BuildConfiguration());

        options.Resolve(nameof(MobileApiOptions.StudentsApi)).Should().BeNull();
    }

    [TestMethod]
    public void Resolve_ReturnsNull_ForABlankApi()
    {
        var options = MobileApiOptions.FromConfiguration(BuildConfiguration(
            ("MobileApi:StudentsApi", "   ")));

        options.Resolve(nameof(MobileApiOptions.StudentsApi)).Should().BeNull();
    }

    [TestMethod]
    public void MissingBaseAddresses_ListsOnlyTheUnconfiguredApis_InApiNamesOrder()
    {
        var options = MobileApiOptions.FromConfiguration(BuildConfiguration(
            ("MobileApi:StudentsApi", Root("students.example", 7450))));

        options.MissingBaseAddresses().Should().Equal(
            nameof(MobileApiOptions.SettingsApi),
            nameof(MobileApiOptions.AssignmentsApi));
    }

    [TestMethod]
    public void Resolve_RejectsANonAbsoluteValue()
    {
        var options = MobileApiOptions.FromConfiguration(BuildConfiguration(
            ("MobileApi:SettingsApi", "settings-api")));

        Action resolve = () => options.Resolve(nameof(MobileApiOptions.SettingsApi));

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*MobileApi:SettingsApi*");
    }

    [TestMethod]
    public void Resolve_RejectsAnUnknownApiName()
    {
        var options = new MobileApiOptions();

        Action resolve = () => options.Resolve("GradesApi");

        resolve.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("apiName");
    }

    [TestMethod]
    public void FromConfiguration_RejectsANullConfiguration()
    {
        Action bind = () => MobileApiOptions.FromConfiguration(null!);

        bind.Should().Throw<ArgumentNullException>();
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    /// <summary>An address with no path, as the AppHost publishes it (e.g. <c>students-api</c>).</summary>
    private static string Root(string host, int port) =>
        new UriBuilder(Uri.UriSchemeHttps, host, port).Uri.ToString();

    private static string WithPath(string host, int port, string path) =>
        new UriBuilder(Uri.UriSchemeHttps, host, port, path).Uri.ToString();
}
