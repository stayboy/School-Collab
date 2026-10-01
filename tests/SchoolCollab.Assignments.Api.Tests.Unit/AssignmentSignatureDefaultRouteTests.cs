using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// The <c>GET /assignments/signature-default</c> endpoint's always-200 <b>wire contract</b>, pinned
/// at the route rather than at the resolver (a resolver unit test cannot reach an endpoint, hence
/// the minimal TestServer harness — the <c>WardRoutesTests</c> precedent, <c>Program</c> is never
/// started).
///
/// <para>Round A pinned the single boolean. Round B1 (D2, Plan (d)#11) widens the body
/// <b>additively</b> with <c>signatureMode</c> (the enum name, via the type-level
/// <c>JsonStringEnumConverter</c>) so the create wizard can distinguish <c>Mandatory</c> (lock the
/// checkbox) from <c>Optional</c> (pre-fill only), while <c>requiresSignature</c> keeps its
/// derivation <c>SignatureRequirement != Disabled</c> — a consumer reading only the boolean still
/// parses the body.</para>
/// </summary>
[TestClass]
public class AssignmentSignatureDefaultRouteTests
{
    private static readonly Guid GradeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.DisableOIDCAuth;
        public IDictionary<string, bool> GetAllFlags() =>
            new Dictionary<string, bool> { [FeatureFlagKeys.DisableOIDCAuth] = true };
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEnabled(featureKey));
    }

    private sealed class StubAssignmentPolicyResolver(EffectiveAssignmentPolicy policy) : IAssignmentPolicyResolver
    {
        public Guid? SeenGradeLevelId { get; private set; }

        public Task<EffectiveAssignmentPolicy> ResolveAsync(
            Guid? gradeLevelId, CancellationToken cancellationToken = default)
        {
            SeenGradeLevelId = gradeLevelId;
            return Task.FromResult(policy);
        }
    }

    private static EffectiveAssignmentPolicy PolicyWith(SignatureRequirementMode mode) =>
        new EffectiveAssignmentPolicyResolver().Resolve(
            new AssignmentPolicyFields { SignatureRequirement = mode }, gradeOverride: null);

    private static async Task<(WebApplication App, HttpClient Client, StubAssignmentPolicyResolver Resolver)>
        StartHostAsync(SignatureRequirementMode mode)
    {
        var resolver = new StubAssignmentPolicyResolver(PolicyWith(mode));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        builder.Services.AddSingleton<IAssignmentPolicyResolver>(resolver);

        var app = builder.Build();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();

        var client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();
        return (app, client, resolver);
    }

    private static async Task<string> GetSignatureDefaultBodyAsync(SignatureRequirementMode mode)
    {
        var (app, client, _) = await StartHostAsync(mode);
        await using (app)
        {
            var response = await client.GetAsync($"/assignments/signature-default?gradeLevelId={GradeId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "the create-wizard pre-fill is always 200 (fail-open resolution)");
            return await response.Content.ReadAsStringAsync();
        }
    }

    [TestMethod]
    public async Task SignatureDefault_Mandatory_ReportsTheModeAndRequiresSignatureTrue()
    {
        var body = await GetSignatureDefaultBodyAsync(SignatureRequirementMode.Mandatory);

        body.Replace(" ", string.Empty).Should().Be(
            "{\"requiresSignature\":true,\"signatureMode\":\"Mandatory\"}",
            "the mode is the additive member the wizard locks the checkbox from; the boolean keeps its derivation");
    }

    [TestMethod]
    public async Task SignatureDefault_Optional_ReportsTheModeAndRequiresSignatureTrue()
    {
        var body = await GetSignatureDefaultBodyAsync(SignatureRequirementMode.Optional);

        body.Replace(" ", string.Empty).Should().Be(
            "{\"requiresSignature\":true,\"signatureMode\":\"Optional\"}");
    }

    [TestMethod]
    public async Task SignatureDefault_Disabled_ReportsTheModeAndRequiresSignatureFalse()
    {
        var body = await GetSignatureDefaultBodyAsync(SignatureRequirementMode.Disabled);

        body.Replace(" ", string.Empty).Should().Be(
            "{\"requiresSignature\":false,\"signatureMode\":\"Disabled\"}");
    }

    [TestMethod]
    public async Task SignatureDefault_RequiresSignatureStaysTheModesDerivation()
    {
        // The additive widening must not change the boolean a legacy consumer reads: for every
        // mode the two members agree (requiresSignature == (mode != Disabled)).
        foreach (var mode in Enum.GetValues<SignatureRequirementMode>())
        {
            var body = await GetSignatureDefaultBodyAsync(mode);
            var expected = mode != SignatureRequirementMode.Disabled ? "true" : "false";

            body.Should().Contain($"\"requiresSignature\":{expected}",
                $"a consumer reading only the boolean sees the pre-widening derivation for {mode}");
            body.Should().Contain($"\"signatureMode\":\"{mode}\"");
        }
    }

    [TestMethod]
    public async Task SignatureDefault_PassesTheGradeThroughToTheResolver()
    {
        // Arrange
        var (app, client, resolver) = await StartHostAsync(SignatureRequirementMode.Optional);

        // Act
        await using (app)
        {
            await client.GetAsync($"/assignments/signature-default?gradeLevelId={GradeId}");
        }

        // Assert
        resolver.SeenGradeLevelId.Should().Be(GradeId,
            "the grade override is resolved from the query string");
    }

    [TestMethod]
    public async Task SignatureDefault_NoGrade_ResolvesTheTenantDefaultOnly()
    {
        // Arrange
        var (app, client, resolver) = await StartHostAsync(SignatureRequirementMode.Optional);

        // Act
        await using (app)
        {
            var response = await client.GetAsync("/assignments/signature-default");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Assert
        resolver.SeenGradeLevelId.Should().BeNull();
    }
}
