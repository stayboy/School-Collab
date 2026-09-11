using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-C1 (spec §7 Q1) coverage for the HTTP-backed
/// <see cref="SignatureDefaultResolver"/>: effective resolution across the
/// settings-api tenant default + students-api grade override, with the
/// fail-open degradation posture (tenant fetch failure ⇒ false; grade fetch
/// failure ⇒ inherit).
/// </summary>
[TestClass]
public class SignatureDefaultResolverTests
{
    private static readonly Guid GradeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static SignatureDefaultResolver NewResolver(
        MockHttpMessageHandler settingsApi, MockHttpMessageHandler studentsApi)
    {
        var factory = new StubHttpClientFactory(settingsApi, studentsApi);
        return new SignatureDefaultResolver(factory, NullLogger<SignatureDefaultResolver>.Instance);
    }

    [TestMethod]
    public async Task Resolve_NoGrade_ReturnsTenantDefault()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/assignment-policy")
            .Respond("application/json", JsonSerializer.Serialize(
                new TenantAssignmentPolicyDto(RequiresSignatureDefault: true), JsonOptions));
        var students = new MockHttpMessageHandler();
        var resolver = NewResolver(settings, students);

        var result = await resolver.ResolveRequiresSignatureDefaultAsync(null);
        result.Should().BeTrue();
    }

    [TestMethod]
    public async Task Resolve_TenantUnset_NoGrade_ReturnsFalse()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/assignment-policy")
            .Respond(HttpStatusCode.NoContent);
        var students = new MockHttpMessageHandler();
        var resolver = NewResolver(settings, students);

        var result = await resolver.ResolveRequiresSignatureDefaultAsync(null);
        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task Resolve_GradeOverrideWins_OverTenantDefault()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/assignment-policy")
            .Respond("application/json", JsonSerializer.Serialize(
                new TenantAssignmentPolicyDto(RequiresSignatureDefault: true), JsonOptions));
        var students = new MockHttpMessageHandler();
        students.When($"http://students-api/students/grade-levels/{GradeId}/assignment-policy")
            .Respond("application/json", JsonSerializer.Serialize(
                new GradeAssignmentPolicyDto(GradeId, RequiresSignatureDefault: false, DateTimeOffset.UtcNow), JsonOptions));
        var resolver = NewResolver(settings, students);

        var result = await resolver.ResolveRequiresSignatureDefaultAsync(GradeId);
        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task Resolve_GradeInheritNull_FallsBackToTenant()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/assignment-policy")
            .Respond("application/json", JsonSerializer.Serialize(
                new TenantAssignmentPolicyDto(RequiresSignatureDefault: true), JsonOptions));
        var students = new MockHttpMessageHandler();
        // 204 = no override row for the grade ⇒ inherit the tenant default.
        students.When($"http://students-api/students/grade-levels/{GradeId}/assignment-policy")
            .Respond(HttpStatusCode.NoContent);
        var resolver = NewResolver(settings, students);

        var result = await resolver.ResolveRequiresSignatureDefaultAsync(GradeId);
        result.Should().BeTrue();
    }

    [TestMethod]
    public async Task Resolve_SettingsUnreachable_DegradesToFalse()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/assignment-policy")
            .Throw(new HttpRequestException("settings-api unreachable"));
        var students = new MockHttpMessageHandler();
        var resolver = NewResolver(settings, students);

        var result = await resolver.ResolveRequiresSignatureDefaultAsync(null);
        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task Resolve_StudentsUnreachable_DegradesToTenantDefault()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/assignment-policy")
            .Respond("application/json", JsonSerializer.Serialize(
                new TenantAssignmentPolicyDto(RequiresSignatureDefault: true), JsonOptions));
        var students = new MockHttpMessageHandler();
        students.When($"http://students-api/students/grade-levels/{GradeId}/assignment-policy")
            .Throw(new HttpRequestException("students-api unreachable"));
        var resolver = NewResolver(settings, students);

        var result = await resolver.ResolveRequiresSignatureDefaultAsync(GradeId);
        result.Should().BeTrue();
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