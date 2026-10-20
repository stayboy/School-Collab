using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;
using EditPage = SchoolCollab.Assignments.Application.Components.Pages.Assignments.Edit;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>assignment-approval-workflow</c> (spec §3.2/§3.6, WS6) — the Edit page's
/// <b>"Locked — pending approval"</b> banner. The server-side hold refuses every save with 409, so
/// the page must say why up front instead of letting the author discover it through a failed save.
///
/// <para>Discriminating: the banner is gated on the loaded row's approval state, not on the route or
/// the authoring component's own mode — a Draft awaiting approval renders it, and a rejected Draft
/// (editable again) does not.</para>
/// </summary>
[TestClass]
public class EditPageLockBannerTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter<AssignmentTypeDto>(),
            new JsonStringEnumConverter<AssignmentStatusDto>(),
            new JsonStringEnumConverter<GradingFormatDto>(),
            new JsonStringEnumConverter<TargetAudienceTypeDto>(),
            new JsonStringEnumConverter<ApprovalStatusDto>(),
        }
    };

    public EditPageLockBannerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<EditPage>>());
        Services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider());
        // The embedded authoring component's injected surface (the Detail suite's set).
        Services.AddSingleton<SchoolCollab.Core.Features.IFeatureFlagService>(new StubFeatureFlags());
        Services.AddSingleton<IAssignmentQuestionGenerator>(Mock.Of<IAssignmentQuestionGenerator>());
        Services.AddSingleton<IUrlTextExtractor>(Mock.Of<IUrlTextExtractor>());
        Services.AddSingleton(Mock.Of<ILogger<SchoolCollab.Assignments.Application.Components.Pages.Assignments.AssignmentAuthoring>>());
        Services.AddSingleton<SchoolCollab.Students.Application.Services.StudentsApiClient>();
        Services.AddSingleton<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<SchoolCollab.Students.Application.Services.StudentsApiClient>>());
        Services.AddSingleton<CertificateDownloadService>();
        Services.AddSingleton(Mock.Of<ILogger<CertificateDownloadService>>());
    }

    private AssignmentSummaryDto MakeDto(ApprovalStatusDto? approvalStatus, DateTimeOffset? windowExpiresAt = null) =>
        new(
            Guid.NewGuid(), "Math HW", null, AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded,
            TargetAudienceTypeDto.AllStudents, Guid.NewGuid(), "Math", AssignmentStatusDto.Draft,
            null, null, false, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            ApprovalStatus: approvalStatus,
            RequiresApproval: true,
            WindowExpiresAt: windowExpiresAt);

    private void SetupReads(AssignmentSummaryDto dto)
    {
        // The page's own read is the one under test; the embedded authoring component's companion
        // reads are stubbed empty so only the banner is observable.
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/questions-draft")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/effective-policy")
            .Respond(HttpStatusCode.OK, "application/json",
                "{\"signatureRequirement\":\"Disabled\",\"requiresApprovalBeforePublish\":true," +
                "\"maxPrimaryContacts\":null,\"maxCopyContacts\":null,\"mandatoryReview\":null," +
                "\"archiveGraceDays\":null,\"signatureRequirementFromOverride\":false," +
                "\"requiresApprovalBeforePublishFromOverride\":false,\"maxPrimaryContactsFromOverride\":false," +
                "\"maxCopyContactsFromOverride\":false,\"mandatoryReviewFromOverride\":false," +
                "\"archiveGraceDaysFromOverride\":false}");
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/ai-prompt-policy")
            .Respond(HttpStatusCode.OK, "application/json", "{\"aiPromptLocked\":false}");
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/grade-levels")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/subjects/by-grade/*")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, "http://localhost/activity-groups*")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(dto, _apiJsonOptions));
    }

    [TestMethod]
    public void Edit_PendingApproval_RendersTheLockBanner_WithTheWindowHint()
    {
        var dto = MakeDto(ApprovalStatusDto.Pending, windowExpiresAt: DateTimeOffset.UtcNow.AddDays(2));
        SetupReads(dto);

        var cut = Render<EditPage>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Locked — pending approval"),
            TimeSpan.FromSeconds(15));
        cut.Markup.Should().Contain("awaiting an approver");
        cut.Markup.Should().Contain("approval window expires",
            "the author is told how long the lock is expected to last when a window is configured");
    }

    [TestMethod]
    public void Edit_RejectedApproval_RendersNoLockBanner()
    {
        // Rejected rows are editable + resubmittable again — the hold is released.
        var dto = MakeDto(ApprovalStatusDto.Rejected);
        SetupReads(dto);

        var cut = Render<EditPage>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() => _mockHttp.VerifyNoOutstandingRequest(), TimeSpan.FromSeconds(15));
        cut.Markup.Should().NotContain("Locked — pending approval");
    }

    [TestMethod]
    public void Edit_NotSubmitted_RendersNoLockBanner()
    {
        var dto = MakeDto(approvalStatus: null);
        SetupReads(dto);

        var cut = Render<EditPage>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() => _mockHttp.VerifyNoOutstandingRequest(), TimeSpan.FromSeconds(15));
        cut.Markup.Should().NotContain("Locked — pending approval");
    }

    private sealed class StubFeatureFlags : SchoolCollab.Core.Features.IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => false;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private sealed class StubAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new System.Security.Claims.ClaimsPrincipal()));
    }
}
