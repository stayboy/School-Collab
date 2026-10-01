using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round A (<c>documents/solution/assignment-policy-fields.md</c> §5) coverage for the HTTP-backed
/// <see cref="AssignmentPolicyResolver"/> (the resolver that replaced
/// <c>SignatureDefaultResolver</c>): effective resolution across the settings-api tenant default +
/// students-api grade override, with the fail-open degradation posture (tenant fetch failure ⇒
/// nothing configured; grade fetch failure / 204 ⇒ inherit) and the enum's wire form.
/// </summary>
[TestClass]
public class AssignmentPolicyResolverTests
{
    private static readonly Guid GradeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string TenantUrl = "http://settings-api/api/settings/assignment-policy";

    private static string GradeUrl => $"http://students-api/students/grade-levels/{GradeId}/assignment-policy";

    private static AssignmentPolicyResolver NewResolver(
        MockHttpMessageHandler settingsApi, MockHttpMessageHandler studentsApi)
    {
        var factory = new StubHttpClientFactory(settingsApi, studentsApi);
        return new AssignmentPolicyResolver(factory, NullLogger<AssignmentPolicyResolver>.Instance);
    }

    private static string TenantJson(TenantAssignmentPolicyDto dto) => JsonSerializer.Serialize(dto, JsonOptions);

    private static string GradeJson(GradeAssignmentPolicyDto dto) => JsonSerializer.Serialize(dto, JsonOptions);

    [TestMethod]
    public void SignatureRequirementMode_TravelsAsItsName_NotANumber()
    {
        // Arrange — the shared enum carries a type-level JsonStringEnumConverter so every host and
        // client agrees on the shape without a per-host registration (see the enum's XML doc).
        var json = TenantJson(new TenantAssignmentPolicyDto(
            SignatureRequirementMode.Mandatory, null, null, null));

        // Act / Assert
        json.Should().Contain("\"signatureRequirement\":\"Mandatory\"");
        json.Should().NotMatchRegex("\"signatureRequirement\":\\s*\\d");
    }

    [TestMethod]
    public async Task Resolve_TenantConfiguredNoGrade_ReturnsTheTenantFields()
    {
        // Arrange
        var settings = new MockHttpMessageHandler();
        settings.When(TenantUrl).Respond("application/json", TenantJson(new TenantAssignmentPolicyDto(
            SignatureRequirementMode.Optional, RequiresApprovalBeforePublish: true,
            MaxPrimaryContacts: 2, MaxCopyContacts: 4)));
        var students = new MockHttpMessageHandler();
        var resolver = NewResolver(settings, students);

        // Act
        var policy = await resolver.ResolveAsync(null);

        // Assert
        policy.SignatureRequirement.Should().Be(SignatureRequirementMode.Optional);
        policy.RequiresApprovalBeforePublish.Should().BeTrue();
        policy.MaxPrimaryContacts.Should().Be(2);
        policy.MaxCopyContacts.Should().Be(4);
        policy.SignatureRequirementFromOverride.Should().BeFalse("there is no grade to override from");
    }

    [TestMethod]
    public async Task Resolve_TenantUnsetNoGrade_FallsBackToTheBuiltInDefaults()
    {
        // Arrange — 204 = no policy row for this tenant yet.
        var settings = new MockHttpMessageHandler();
        settings.When(TenantUrl).Respond(HttpStatusCode.NoContent);
        var students = new MockHttpMessageHandler();
        var resolver = NewResolver(settings, students);

        // Act
        var policy = await resolver.ResolveAsync(null);

        // Assert
        policy.SignatureRequirement.Should().Be(SignatureRequirementMode.Disabled,
            "the old default-false is now Disabled");
        policy.RequiresApprovalBeforePublish.Should().BeFalse();
        policy.MaxPrimaryContacts.Should().BeNull();
        policy.MaxCopyContacts.Should().BeNull();
    }

    [TestMethod]
    public async Task Resolve_GradeOverrideWins_OverTheTenantDefault()
    {
        // Arrange
        var settings = new MockHttpMessageHandler();
        settings.When(TenantUrl).Respond("application/json", TenantJson(new TenantAssignmentPolicyDto(
            SignatureRequirementMode.Disabled, null, MaxPrimaryContacts: 5, MaxCopyContacts: 9)));
        var students = new MockHttpMessageHandler();
        students.When(GradeUrl).Respond("application/json", GradeJson(new GradeAssignmentPolicyDto(
            GradeId, SignatureRequirementMode.Mandatory, RequiresApprovalBeforePublish: true,
            MaxPrimaryContacts: 1, MaxCopyContacts: null, DateTimeOffset.UtcNow)));
        var resolver = NewResolver(settings, students);

        // Act
        var policy = await resolver.ResolveAsync(GradeId);

        // Assert
        policy.SignatureRequirement.Should().Be(SignatureRequirementMode.Mandatory);
        policy.RequiresApprovalBeforePublish.Should().BeTrue();
        policy.MaxPrimaryContacts.Should().Be(1);
        policy.MaxCopyContacts.Should().Be(9, "a null grade field inherits the tenant value");
        policy.MaxCopyContactsFromOverride.Should().BeFalse();
        policy.SignatureRequirementFromOverride.Should().BeTrue();
    }

    [TestMethod]
    public async Task Resolve_Grade204_InheritsTheTenantDefault()
    {
        // Arrange
        var settings = new MockHttpMessageHandler();
        settings.When(TenantUrl).Respond("application/json", TenantJson(new TenantAssignmentPolicyDto(
            SignatureRequirementMode.Optional, RequiresApprovalBeforePublish: true, null, null)));
        var students = new MockHttpMessageHandler();
        students.When(GradeUrl).Respond(HttpStatusCode.NoContent);
        var resolver = NewResolver(settings, students);

        // Act
        var policy = await resolver.ResolveAsync(GradeId);

        // Assert
        policy.SignatureRequirement.Should().Be(SignatureRequirementMode.Optional);
        policy.RequiresApprovalBeforePublish.Should().BeTrue();
        policy.SignatureRequirementFromOverride.Should().BeFalse("the grade has no override row");
    }

    [TestMethod]
    public async Task Resolve_SettingsUnreachable_DegradesToTheBuiltInDefaults()
    {
        // Arrange
        var settings = new MockHttpMessageHandler();
        settings.When(TenantUrl).Throw(new HttpRequestException("settings-api unreachable"));
        var students = new MockHttpMessageHandler();
        var resolver = NewResolver(settings, students);

        // Act
        var policy = await resolver.ResolveAsync(null);

        // Assert
        policy.Should().Be(FakeBuiltIns.DisableApproval,
            "fail-open: a Settings outage must not enable the approval gate");
    }

    [TestMethod]
    public async Task Resolve_StudentsUnreachable_DegradesToTheTenantDefault()
    {
        // Arrange
        var settings = new MockHttpMessageHandler();
        settings.When(TenantUrl).Respond("application/json", TenantJson(new TenantAssignmentPolicyDto(
            SignatureRequirementMode.Mandatory, RequiresApprovalBeforePublish: true, null, null)));
        var students = new MockHttpMessageHandler();
        students.When(GradeUrl).Throw(new HttpRequestException("students-api unreachable"));
        var resolver = NewResolver(settings, students);

        // Act
        var policy = await resolver.ResolveAsync(GradeId);

        // Assert
        policy.SignatureRequirement.Should().Be(SignatureRequirementMode.Mandatory);
        policy.RequiresApprovalBeforePublish.Should().BeTrue();
        policy.SignatureRequirementFromOverride.Should().BeFalse();
    }

    /// <summary>The built-in defaults the resolver must produce when a fetch fails.</summary>
    private static class FakeBuiltIns
    {
        public static readonly EffectiveAssignmentPolicy DisableApproval =
            new EffectiveAssignmentPolicyResolver().Resolve(tenantDefault: null, gradeOverride: null);
    }

    /// <summary>
    /// Minimal <see cref="IHttpClientFactory"/> stub returning MockHttp-backed
    /// clients for the two named clients the resolver uses.
    /// </summary>
    private sealed class StubHttpClientFactory(
        MockHttpMessageHandler settingsApi, MockHttpMessageHandler studentsApi) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var handler = name == "settings-api" ? settingsApi : studentsApi;
            var client = handler.ToHttpClient();
            client.BaseAddress = new Uri(name == "settings-api"
                ? "http://settings-api" : "http://students-api");
            return client;
        }
    }
}
