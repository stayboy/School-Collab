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
/// The <c>GET /assignments/effective-policy</c> endpoint's always-200 <b>wire contract</b>, pinned at
/// the route rather than at the resolver (a resolver unit test cannot reach an endpoint, hence the
/// minimal TestServer harness — the <c>WardRoutesTests</c> precedent, <c>Program</c> is never started).
///
/// <para>Round <c>assignment-rules-policy-rework</c> (D2/OD4) replaces the retired
/// <c>/assignments/signature-default</c> route — whose single boolean plus <c>signatureMode</c> could
/// not feed the new Rules readouts — with the WHOLE resolved policy. The body therefore carries the
/// resolved signature requirement, the D4-derived guardian-review value and the archive window,
/// alongside the pre-existing approval / contact-cap fields and their per-field override flags, so the
/// page never recomputes any part of the resolution.</para>
/// </summary>
[TestClass]
public class AssignmentEffectivePolicyRouteTests
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

    /// <summary>The resolved policy the real resolver derives for a tenant that sets these fields —
    /// so the wire assertions below cannot drift from the resolver's own merge/D4 derivation.</summary>
    private static EffectiveAssignmentPolicy PolicyWith(
        SignatureRequirementMode signature = SignatureRequirementMode.Disabled,
        bool? mandatoryReview = null,
        int? archiveGraceDays = null) =>
        new EffectiveAssignmentPolicyResolver().Resolve(
            tenantDefault: new AssignmentPolicyFields
            {
                SignatureRequirement = signature,
                MandatoryReview = mandatoryReview,
                ArchiveGraceDays = archiveGraceDays,
            },
            gradeOverride: null);

    private static async Task<(WebApplication App, HttpClient Client, StubAssignmentPolicyResolver Resolver)>
        StartHostAsync(EffectiveAssignmentPolicy policy)
    {
        var resolver = new StubAssignmentPolicyResolver(policy);

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

    private static async Task<string> GetEffectivePolicyBodyAsync(EffectiveAssignmentPolicy policy)
    {
        var (app, client, _) = await StartHostAsync(policy);
        await using (app)
        {
            var response = await client.GetAsync($"/assignments/effective-policy?gradeLevelId={GradeId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "the page's policy read is always 200 (fail-open resolution)");
            return await response.Content.ReadAsStringAsync();
        }
    }

    /// <summary>The whole resolved policy crosses the wire — every resolved value plus its per-field
    /// override flag, exactly as the record is shaped. The D4 implication is already applied here
    /// (the signature requirement pins guardian review ON), so the page must never re-derive it.</summary>
    [TestMethod]
    public async Task EffectivePolicy_Mandatory_ReportsTheWholeResolvedPolicyWithTheD4Implication() =>
        (await GetEffectivePolicyBodyAsync(PolicyWith(SignatureRequirementMode.Mandatory)))
            .Replace(" ", string.Empty).Should().Be("""
            {
              "signatureRequirement": "Mandatory",
              "requiresApprovalBeforePublish": false,
              "maxPrimaryContacts": null,
              "maxCopyContacts": null,
              "mandatoryReview": true,
              "archiveGraceDays": null,
              "signatureRequirementFromOverride": false,
              "requiresApprovalBeforePublishFromOverride": false,
              "maxPrimaryContactsFromOverride": false,
              "maxCopyContactsFromOverride": false,
              "mandatoryReviewFromOverride": false,
              "archiveGraceDaysFromOverride": false
            }
            """.Replace(" ", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty));

    /// <summary>D3/D5: the two new policy fields travel on the route like every other field, and an
    /// unset level stays <c>null</c> (the write seam, not the wire, supplies the built-in fallbacks).</summary>
    [TestMethod]
    public async Task EffectivePolicy_ReportsTheGuardianReviewAndArchiveWindowFields()
    {
        var body = await GetEffectivePolicyBodyAsync(
            PolicyWith(SignatureRequirementMode.Disabled, mandatoryReview: false, archiveGraceDays: 45));

        body.Should().Contain("\"mandatoryReview\":false",
            "an explicit policy review value is reported as stored");
        body.Should().Contain("\"archiveGraceDays\":45");
        body.Should().Contain("\"signatureRequirement\":\"Disabled\"");
    }

    [TestMethod]
    public async Task EffectivePolicy_PassesTheGradeThroughToTheResolver()
    {
        // Arrange
        var (app, client, resolver) = await StartHostAsync(PolicyWith(SignatureRequirementMode.Optional));

        // Act
        await using (app)
        {
            await client.GetAsync($"/assignments/effective-policy?gradeLevelId={GradeId}");
        }

        // Assert
        resolver.SeenGradeLevelId.Should().Be(GradeId,
            "the grade override is resolved from the query string");
    }

    [TestMethod]
    public async Task EffectivePolicy_NoGrade_ResolvesTheTenantDefaultOnly()
    {
        // Arrange
        var (app, client, resolver) = await StartHostAsync(PolicyWith(SignatureRequirementMode.Optional));

        // Act
        await using (app)
        {
            var response = await client.GetAsync("/assignments/effective-policy");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Assert
        resolver.SeenGradeLevelId.Should().BeNull();
    }

    /// <summary>OD4: the retired route is really gone — a request for it no longer resolves (the
    /// literal segment used to win over the <c>{id:guid}</c> template).</summary>
    [TestMethod]
    public async Task SignatureDefaultRoute_IsDeleted()
    {
        // Arrange
        var (app, client, _) = await StartHostAsync(PolicyWith(SignatureRequirementMode.Mandatory));

        // Act
        await using (app)
        {
            var response = await client.GetAsync($"/assignments/signature-default?gradeLevelId={GradeId}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "OD4: the page's policy seam is /assignments/effective-policy; the single-boolean route is retired");
        }
    }
}
