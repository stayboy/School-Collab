using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// R2-7 (TGT-16 / D-4): wire tests for <code>GET /assignments/recipient-preview</code>.
/// Covers the full query binding (the posted grade/stream/student/group target arrays), the
/// happy-path count shape, and the degraded shape — HTTP 200 with all-zero counts and
/// <code>PreviewDegraded = true</code>, never an exception.
/// Round <c>drop-primary-grade</c>: the optional <code>primaryGradeId</code> is gone — publish's
/// teacher cohort and both policy legs are derived from the posted grade targets, and the preview
/// reproduces that derivation.
/// </summary>
[TestClass]
public class AssignmentRecipientPreviewRouteTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Student1 = Guid.Parse("33333333-3333-3333-3333-333333333331");
    private static readonly Guid Student2 = Guid.Parse("33333333-3333-3333-3333-333333333332");
    private static readonly Guid Contact1 = Guid.Parse("66666666-6666-6666-6666-666666666661");
    private static readonly Guid Contact2 = Guid.Parse("66666666-6666-6666-6666-666666666662");

    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.DisableOIDCAuth;
        public IDictionary<string, bool> GetAllFlags() =>
            new Dictionary<string, bool> { [FeatureFlagKeys.DisableOIDCAuth] = true };
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEnabled(featureKey));
    }

    private sealed class StubTenantProvider : ITenantProvider
    {
        public TenantContext GetTenantContext() => new(TenantId, "TestSchool", TenantType.School);
    }

    private sealed class CapturingTargetResolver : IAssignmentTargetResolver
    {
        public IReadOnlyList<TargetConstraint>? LastConstraints { get; private set; }
        public bool? LastIncludeAllStudents { get; private set; }
        public Guid[] StudentIds { get; set; } = [];
        public Exception? Throw { get; set; }

        public Task<Guid[]> ResolveStudentIdsAsync(
            IReadOnlyList<TargetConstraint> targets, bool includeAllStudents, CancellationToken ct = default)
        {
            LastConstraints = targets;
            LastIncludeAllStudents = includeAllStudents;
            return Throw is null ? Task.FromResult(StudentIds) : Task.FromException<Guid[]>(Throw);
        }
    }

    private sealed class CapturingContactResolver : IContactResolver
    {
        public ResolveSubscribersRequest? LastRequest { get; private set; }
        public IReadOnlyList<SubscriberInfo> Subscribers { get; set; } = [];

        public Task<IReadOnlyList<SubscriberInfo>> ResolveSubscribersAsync(
            ResolveSubscribersRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(Subscribers);
        }
    }

    private sealed class StubNotificationPolicyResolver : INotificationPolicyResolver
    {
        public Task<EffectiveNotificationPolicy> ResolveEffectiveAsync(
            Guid tenantId, Guid? gradeLevelId, CancellationToken ct = default) =>
            Task.FromResult(new EffectiveNotificationPolicy(
                [NotificationChannel.Email], [], null, null, null, null, null, null,
                false, false, false, false, false, false, false, false));
    }

    private sealed class StubAssignmentPolicyResolver : IAssignmentPolicyResolver
    {
        public Task<EffectiveAssignmentPolicy> ResolveAsync(
            Guid? gradeLevelId, CancellationToken ct = default) =>
            Task.FromResult(new EffectiveAssignmentPolicyResolver().Resolve(
                    AssignmentPolicyFields.Empty, gradeOverride: null));
    }

    private static async Task<(WebApplication App, HttpClient Client, CapturingTargetResolver Resolver, CapturingContactResolver Contacts)>
        StartHostAsync(
            Guid[]? studentIds = null,
            IReadOnlyList<SubscriberInfo>? subscribers = null,
            Exception? resolverFailure = null)
    {
        var resolver = new CapturingTargetResolver { StudentIds = studentIds ?? [Student1, Student2] };
        if (resolverFailure is not null) resolver.Throw = resolverFailure;

        var contacts = new CapturingContactResolver
        {
            Subscribers = subscribers ??
            [
                new SubscriberInfo(Contact1, ContactOwnerType.Guardian, Guid.NewGuid(), Student1, ContactChannel.Email, GuardianRole.Primary),
                new SubscriberInfo(Contact2, ContactOwnerType.Guardian, Guid.NewGuid(), Student2, ContactChannel.Email, GuardianRole.CC)
            ]
        };

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        builder.Services.AddSingleton<IAssignmentTargetResolver>(resolver);
        builder.Services.AddSingleton<IContactResolver>(contacts);
        builder.Services.AddSingleton<INotificationPolicyResolver>(new StubNotificationPolicyResolver());
        builder.Services.AddSingleton<IAssignmentPolicyResolver>(new StubAssignmentPolicyResolver());
        builder.Services.AddSingleton<ITenantProvider>(new StubTenantProvider());

        var app = builder.Build();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();

        var client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();
        return (app, client, resolver, contacts);
    }

    private static async Task<RecipientPreviewDto> GetPreviewAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/assignments/recipient-preview" + query);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<RecipientPreviewDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Could not deserialize preview response");
    }

    [TestMethod]
    public async Task RecipientPreview_BindsAllQueryArrays_AndFeedsThePostedGradesToTheTeacherCohortLeg()
    {
        var (app, client, resolver, contacts) = await StartHostAsync();
        await using (app)
        {
            var grade = Guid.NewGuid();
            var stream = Guid.NewGuid();
            var student = Guid.NewGuid();
            var group = Guid.NewGuid();

            _ = await GetPreviewAsync(client,
                $"?allStudents=false&gradeLevelIds={grade}&streamCodedValueIds={stream}&studentIds={student}&activityGroupIds={group}");

            resolver.LastIncludeAllStudents.Should().BeFalse();
            resolver.LastConstraints.Should().Contain(c => c.Kind == TargetKind.GradeLevel && c.RefId == grade);
            resolver.LastConstraints.Should().Contain(c => c.Kind == TargetKind.Stream && c.RefId == stream);
            resolver.LastConstraints.Should().Contain(c => c.Kind == TargetKind.Student && c.RefId == student);
            resolver.LastConstraints.Should().Contain(c => c.Kind == TargetKind.ActivityGroup && c.RefId == group);
            contacts.LastRequest.Should().NotBeNull();
            contacts.LastRequest!.GradeLevelIds.Should().BeEquivalentTo(
                new[] { grade },
                "the preview's teacher-cohort input is the POSTED grade-target set (round "
                + "drop-primary-grade: there is no separately-authored primary grade to carry)");
        }
    }

    [TestMethod]
    public async Task RecipientPreview_HappyPath_ReturnsFilteredCounts()
    {
        var (app, client, _, _) = await StartHostAsync();
        await using (app)
        {
            var preview = await GetPreviewAsync(client, "?allStudents=true");

            preview.StudentsMatched.Should().Be(2);
            preview.PrimaryContacts.Should().Be(1);
            preview.OtherContacts.Should().Be(1);
            preview.PreviewDegraded.Should().BeFalse();
        }
    }

    [TestMethod]
    public async Task RecipientPreview_ResolverFailure_Returns200WithZeroCountsAndDegradedFlag()
    {
        var (app, client, _, _) = await StartHostAsync(resolverFailure: new HttpRequestException("students-api unreachable"));
        await using (app)
        {
            var preview = await GetPreviewAsync(client, "?allStudents=true");

            preview.StudentsMatched.Should().Be(0);
            preview.PrimaryContacts.Should().Be(0);
            preview.OtherContacts.Should().Be(0);
            preview.PreviewDegraded.Should().BeTrue("D-4: a resolver failure degrades to all-zero counts, never an exception");
        }
    }
}
