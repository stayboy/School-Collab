using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using DetailPage = SchoolCollab.Assignments.Application.Components.Pages.Assignments.Detail;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.Features;
using DetailPage_Component = SchoolCollab.Assignments.Application.Components.Pages.Assignments.Detail;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>WS-A2 / spec §3.5 step 2 + §7 Q2 — the Detail page
/// surfaces for the new lifecycle states (decision (j)):
/// <list type="bullet">
///   <item>Scheduled renders badge + "Publish now" + "Cancel schedule" + "Available from".</item>
///   <item>Archived renders the Archived badge and NO Edit/Publish/Schedule/Submit controls.</item>
///   <item>Approval section is gated: flag OFF == panel absent; flag ON + Draft == "Submit for Approval"; flag ON + Pending == Approve+Reject buttons (confirmed via dialog).</item>
///   <item>Draft + flag ON == "Schedule…" rendered; the ScheduleDialog flow drives POST /assignments/{id}/schedule.</item>
/// </list>
/// Uses the same Blazor + MockHttp + bUnit pattern as the
/// <c>AssignmentIndexBunitTests</c>; <c>FeatureFlagGate</c> from
/// <c>Admin.Shared</c> re-evaluates on parameter change so the flag
/// in the test DI controls the gating directly.
/// </summary>
[TestClass]
public class AssignmentDetailBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;

    public AssignmentDetailBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        _apiJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
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

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<DetailPage>>());
        // Detail.razor injects StudentsApiClient for the publish dialog contact picker.
        Services.AddSingleton<SchoolCollab.Students.Application.Services.StudentsApiClient>();
        Services.AddSingleton<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<SchoolCollab.Students.Application.Services.StudentsApiClient>>());
        // WS-A2: the Detail page injects IFeatureFlagService to gate the
        // approval panel + Draft row's Schedule action.
        SetupFeatureFlag(false);
    }

    /// <summary>Replaces the registered <see cref="IFeatureFlagService"/>
    /// singleton with a fake at the requested ON/OFF state so the
    /// per-test gating is just call this method before rendering.</summary>
    private void SetupFeatureFlag(bool on)
    {
        var existing = Services.Where(s => s.ServiceType == typeof(IFeatureFlagService)).ToList();
        foreach (var s in existing)
            Services.Remove(s);
        Services.AddSingleton<IFeatureFlagService>(new FakeFeatureFlagService { IsEnabledValue = on });
    }

    /// <summary>GET-hit counter for the assignment endpoint — the approve /
    /// reject / schedule flows reload <c>_item</c> after a successful POST, so a
    /// count of 2+ is the "page reloaded" observable from the plan's binding
    /// coverage list.</summary>
    private int _assignmentGetCount;

    private void SetupGetAssignment(AssignmentSummaryDto dto)
    {
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}")
            .Respond(_ =>
            {
                _assignmentGetCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(dto, _apiJsonOptions),
                        Encoding.UTF8, "application/json")
                };
            });
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/recipients")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(Array.Empty<AssignmentRecipientDto>(), _apiJsonOptions));
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/submissions")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(Array.Empty<SubmissionForReviewDto>(), _apiJsonOptions));
    }

    private static AssignmentSummaryDto MakeDto(AssignmentStatusDto status, ApprovalStatusDto? approvalStatus = null, DateTimeOffset? availableFromUtc = null, DateTimeOffset? dueDate = null, int archiveGraceDays = 30) =>
        new(
            Id: Guid.NewGuid(),
            Title: "Math HW",
            Description: null,
            AssignmentType: AssignmentTypeDto.Digital,
            GradingFormat: GradingFormatDto.TeacherGraded,
            TargetAudienceType: TargetAudienceTypeDto.AllStudents,
            TopicId: Guid.NewGuid(),
            TopicName: "Math",
            GradeLevelId: null,
            GradeName: null,
            Status: status,
            DueDate: dueDate,
            MaxScore: null,
            MandatoryReview: false,
            CreatedByTeacherId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            AvailableFromUtc: availableFromUtc,
            ArchiveGraceDays: archiveGraceDays,
            ApprovalStatus: approvalStatus);

    [TestMethod]
    public void Detail_Scheduled_RendersScheduledBadgeAndActions()
    {
        var dto = MakeDto(AssignmentStatusDto.Scheduled,
            availableFromUtc: DateTimeOffset.UtcNow.AddDays(1));
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Scheduled");
            cut.Markup.Should().Contain("Publish now");
            cut.Markup.Should().Contain("Cancel schedule");
            cut.Markup.Should().Contain("Available from",
                "the Overview grid surfaces the Scheduled availability window (decision (j))");
        });
    }

    [TestMethod]
    public void Detail_Archived_ShowsBadgeAndNoEditOrActions()
    {
        var dto = MakeDto(AssignmentStatusDto.Archived);
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Archived"));
        // No edit / publish / schedule / submit controls.
        cut.Markup.Should().NotContain(">Edit<");
        cut.Markup.Should().NotContain(">Publish<");
        cut.Markup.Should().NotContain("Schedule");
    }

    [TestMethod]
    public void Detail_FlagOff_ApprovalPanelHidden()
    {
        SetupFeatureFlag(false);
        var dto = MakeDto(AssignmentStatusDto.Draft);
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));
        cut.Markup.Should().NotContain("Submit for Approval");
    }

    [TestMethod]
    public void Detail_FlagOn_Draft_RendersSubmitForApproval()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft);
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Submit for Approval"));
    }

    [TestMethod]
    public void Detail_FlagOn_Pending_RendersApproveAndReject()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Pending);
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Approve");
            cut.Markup.Should().Contain("Reject");
        });
    }

    [TestMethod]
    public void Detail_FlagOn_Approved_ShowsReadOnlyChip()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Approved);
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Approved"));
        cut.Markup.Should().NotContain("Submit for Approval");
        cut.Markup.Should().NotContain("Reject");
    }

    [TestMethod]
    public void Detail_DraftFlagOn_ShowsScheduleAction()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft);
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Schedule"));
    }

    // ── Approval chip-text matrix (decision (k) binding coverage) ──

    [TestMethod]
    public void Detail_ApprovalChipText_NotSubmittedWhenApprovalStatusNull()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: null);
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Not submitted",
                "null ApprovalStatus renders the 'Not submitted' chip text");
        });
    }

    [TestMethod]
    public void Detail_ApprovalChipText_PendingRendersPendingText()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Pending);
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Pending"));
    }

    [TestMethod]
    public void Detail_ApprovalChipText_RejectedRendersRejectedText()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Rejected);
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Rejected"));
    }

    // ── UI-tester rework P2: a REJECTED (still-Draft) assignment must
    //    keep a resubmit path in Detail, consistent with the Index
    //    "Submit for Approval" action. The original gate restricted the
    //    button to "ApprovalStatus is null", leaving a rejected draft
    //    with only the chip and no way to resubmit. The fixed gate is
    //    "null or Rejected" so a rejected draft sees the resubmit button.
    [TestMethod]
    public void Detail_FlagOn_RejectedDraft_RendersSubmitForApproval()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Rejected);
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Rejected",
                "the chip still surfaces the rejected state");
            cut.Markup.Should().Contain("Submit for Approval",
                "a rejected Draft must offer the resubmit path in Detail, consistent with the Index");
        });
    }

    // ── Approve confirm/decline flows (decision (k) binding coverage) ──

    /// <summary>Replaces the registered <see cref="IDialogService"/> (the real
    /// FluentUI one added by <c>AddFluentUIComponents</c>) with the mock so the
    /// page's injection resolves it — <c>GetRequiredService</c> returns the
    /// FIRST registration, so the real service must be removed, not
    /// shadowed.</summary>
    private void ReplaceDialogService(Mock<IDialogService> dialogMock)
    {
        var existing = Services.Where(s => s.ServiceType == typeof(IDialogService)).ToList();
        foreach (var s in existing) Services.Remove(s);
        Services.AddSingleton(dialogMock.Object);
    }

    /// <summary>Mocks the confirm dialog (<c>ShowConfirmDialogAsync</c> resolves
    /// to <c>ShowDialogAsync&lt;ConfirmDialog, ConfirmDialogContent&gt;</c>) at the
    /// requested outcome — the ResourcesSectionBunitTests /
    /// QuestionEditorSectionBunitTests pattern. Property-getter setups use
    /// <c>.Returns(Task.FromResult(...))</c>: Moq 4.20 has no ReturnsAsync on
    /// <c>SetupGet</c> receivers.</summary>
    private Mock<IDialogService> SetupConfirmDialogResult(bool confirmed)
    {
        var dialogRef = new Mock<IDialogReference>();
        // Cancelled when the user declines; Ok when they confirm.
        var result = confirmed
            ? DialogResult.Ok<object?>(null)
            : DialogResult.Cancel();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(result));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);
        ReplaceDialogService(dialogMock);
        return dialogMock;
    }

    [TestMethod]
    public void Detail_FlagOn_Pending_ApproveClicked_UserConfirms_PostsApprove()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Pending);
        SetupGetAssignment(dto);
        SetupConfirmDialogResult(confirmed: true);

        // The Expect matches only the POST (the initial GET falls through to
        // the When backend above — the AssignmentIndexBunitTests
        // expect-before-backend ordering note). The body assertion pins the
        // Guid.Empty approver placeholder (decision (f) / spec §7 Q2 identity
        // posture).
        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        // The Approve / Reject buttons carry no title attribute — select by
        // text content (the TopicCreateDialogTests fluent-button precedent).
        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Trim() == "Approve"));

        // v6 ordering rule (the flag-ON Index test's documented constraint): an
        // outstanding Expect 404s every other request, so it is registered
        // only now — the initial GETs are already served by the When backends.
        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/approve")
            .WithContent(JsonSerializer.Serialize(new ApproveAssignmentRequest(Guid.Empty), _apiJsonOptions))
            .Respond(HttpStatusCode.NoContent);

        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Approve").Click();

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            _assignmentGetCount.Should().BeGreaterThanOrEqualTo(2,
                "the page reloads the assignment after the approve POST succeeds");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_FlagOn_Pending_ApproveClicked_UserDeclines_NoPost()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Pending);
        SetupGetAssignment(dto);
        var dialogMock = SetupConfirmDialogResult(confirmed: false);

        // Counting backend that is NOT expected to fire: a wrongly-sent POST
        // succeeds here and becomes observable on the counters below (a NoMatch
        // exception would instead be swallowed into the page error bar).
        var approvePosts = 0;
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/approve")
            .Respond(_ =>
            {
                approvePosts++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Trim() == "Approve"));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Approve").Click();

        // Positive wait first: the confirm dialog opened and resolved (declined).
        cut.WaitForAssertion(() => dialogMock.Verify(
            d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()),
            Times.Once));

        approvePosts.Should().Be(0,
            "declining the confirm dialog must not fire the approve POST");
        _assignmentGetCount.Should().Be(1,
            "declining must not reload the assignment either — only the initial load fired");
    }

    [TestMethod]
    public void Detail_FlagOn_Pending_RejectClicked_UserConfirms_PostsReject()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Pending);
        SetupGetAssignment(dto);
        SetupConfirmDialogResult(confirmed: true);

        // The body assertion pins the Guid.Empty approver placeholder
        // (decision (f) / spec §7 Q2 identity posture).
        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Trim() == "Reject"));

        // v6 ordering rule: register the Expect only now — an outstanding
        // Expect 404s every other request (the flag-ON Index test documents
        // the same constraint), and the initial GETs must hit the When
        // backends first.
        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/reject")
            .WithContent(JsonSerializer.Serialize(new RejectAssignmentRequest(Guid.Empty), _apiJsonOptions))
            .Respond(HttpStatusCode.NoContent);

        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Reject").Click();

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            _assignmentGetCount.Should().BeGreaterThanOrEqualTo(2,
                "the page reloads the assignment after the reject POST succeeds");
        }, TimeSpan.FromSeconds(15));
    }

    // ── Schedule dialog POST flow (decision (k) binding coverage) ──

    /// <summary>Mocks the ScheduleDialog shell open — the ContactsEditorTests
    /// pattern: <c>ShowShellDialogAsync&lt;ScheduleDialog, ScheduleFormModel,
    /// ScheduleResult&gt;</c> resolves to
    /// <c>ShowDialogAsync&lt;ScheduleDialog, DialogShellData&lt;ScheduleFormModel&gt;&gt;</c>,
    /// and the dialog Result carries a <see cref="DialogShellResult{TResult}"/>
    /// the extension unwraps into the typed result.</summary>
    private void SetupScheduleDialogResult(DateTimeOffset availableFromUtc)
    {
        var dialogRef = new Mock<IDialogReference>();
        var payload = new DialogShellResult<ScheduleResult>(new ScheduleResult(availableFromUtc));
        dialogRef.SetupGet(r => r.Result)
            .Returns(Task.FromResult(DialogResult.Ok<object?>(payload)));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ScheduleDialog, DialogShellData<ScheduleFormModel>>(
                It.IsAny<DialogShellData<ScheduleFormModel>>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);
        ReplaceDialogService(dialogMock);
    }

    [TestMethod]
    public void Detail_DraftFlagOn_ScheduleDialogConfirm_PostsScheduleAtUtcMidnight()
    {
        SetupFeatureFlag(true);
        var dto = MakeDto(AssignmentStatusDto.Draft);
        SetupGetAssignment(dto);

        // Pick a future calendar date — the dialog lands at 00:00 UTC of it
        // (decision (j) ScheduleDialog behavior). The mocked dialog result
        // carries the exact 00:00-UTC moment the real ScheduleDialog.SubmitAsync
        // produces from the chosen date.
        var expectedUtc = new DateTimeOffset(new DateTime(2027, 3, 15), TimeSpan.Zero);
        SetupScheduleDialogResult(expectedUtc);

        // The body assertion pins the serialized ScheduleAssignmentRequest. It
        // is built with the same Web-default options the ApiClient uses —
        // hand-rolled "o"-format strings mismatch because System.Text.Json
        // omits zero fractional seconds when writing DateTimeOffset values.
        var expectedJson = JsonSerializer.Serialize(new ScheduleAssignmentRequest(expectedUtc), _apiJsonOptions);
        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Contains("Schedule")));

        // v6 ordering rule: register the Expect only now — an outstanding
        // Expect 404s every other request (the flag-ON Index test documents
        // the same constraint), and the initial GETs must hit the When
        // backends first.
        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/schedule")
            .WithContent(expectedJson)
            .Respond(HttpStatusCode.NoContent);

        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Schedule")).Click();

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            _assignmentGetCount.Should().BeGreaterThanOrEqualTo(2,
                "the page reloads the assignment after the schedule POST succeeds");
        }, TimeSpan.FromSeconds(15));
    }

    // ── WS-A4 / spec §3.1 — duplicate-as-template action ─────────────

    [TestMethod]
    public void Detail_PublishedAssignment_RendersDuplicateButton()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Trim() == "Duplicate",
                "a Published assignment must render the Duplicate button regardless of status");
        });
    }

    [TestMethod]
    public void Detail_DuplicateClicked_UserConfirms_PostsAndNavigatesToCopy()
    {
        var sourceId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var dto = MakeDto(AssignmentStatusDto.Published) with { Id = sourceId };
        SetupGetAssignment(dto);
        SetupConfirmDialogResult(confirmed: true);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));
        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Trim() == "Duplicate"));

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{sourceId}/duplicate")
            .Respond(HttpStatusCode.Created, "application/json",
                JsonSerializer.Serialize(new { id = newId }, _apiJsonOptions));

        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Duplicate").Click();

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"/assignments/{newId}",
                "confirming duplicate must navigate to the new copy");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_DuplicateClicked_UserDeclines_NoPostStaysOnPage()
    {
        var sourceId = Guid.NewGuid();
        var dto = MakeDto(AssignmentStatusDto.Published) with { Id = sourceId };
        SetupGetAssignment(dto);
        var dialogMock = SetupConfirmDialogResult(confirmed: false);

        var postCount = 0;
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{sourceId}/duplicate")
            .Respond(_ =>
            {
                postCount++;
                return new HttpResponseMessage(HttpStatusCode.Created);
            });

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));
        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Trim() == "Duplicate"));

        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Duplicate").Click();

        cut.WaitForAssertion(() => dialogMock.Verify(
            d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()),
            Times.Once));

        postCount.Should().Be(0, "declining the confirm dialog must not fire the duplicate POST");
        Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/",
            "declining must not navigate away from the source assignment page");
    }

    [TestMethod]
    public void Detail_DuplicateClicked_PostFails_ShowsDuplicateError()
    {
        var sourceId = Guid.NewGuid();
        var dto = MakeDto(AssignmentStatusDto.Published) with { Id = sourceId };
        SetupGetAssignment(dto);
        SetupConfirmDialogResult(confirmed: true);

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));
        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Trim() == "Duplicate"));

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{sourceId}/duplicate")
            .Respond(HttpStatusCode.InternalServerError);

        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Duplicate").Click();

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            cut.Markup.Should().Contain("fluent-messagebar",
                "a failing duplicate POST must render the dedicated error bar");
            cut.Markup.Should().Contain("intent-error",
                "the rendered message bar must carry the error intent");
            cut.Markup.Should().MatchRegex("500|Internal Server Error",
                "the error bar must surface the failure message");
        }, TimeSpan.FromSeconds(15));

        Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/",
            "a failure must not navigate away from the source assignment");
    }

    // ── WS-A3 (spec §3.3): versions table Score + Passed columns ──────────

    [TestMethod]
    public void Detail_VersionsTable_RendersScoreAndPassed_WhenSet()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);

        // WS-A3: register the specific /submissions backend BEFORE the generic
        // SetupGetAssignment fallback; MockHttp v6 first-match ordering means
        // the later empty fallback would otherwise win.
        var studentId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var submissions = new[] {
            new SubmissionForReviewDto(submissionId, dto.Id, dto.Title, studentId, 1, ReviewStateDto.Pending, DateTimeOffset.UtcNow),
        };
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/submissions")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(submissions, _apiJsonOptions));

        SetupGetAssignment(dto);

        // Submission detail with two versions — both carry Score/Passed
        // (AutoGraded / InstantGraded path).
        var detail = new SubmissionDetailDto(
            submissionId, dto.Id, studentId, 2, ReviewStateDto.Pending, DateTimeOffset.UtcNow,
            new[]
            {
                new SubmissionVersionDto(Guid.NewGuid(), 1, SubmissionSourceDto.Student, "v1", null, DateTimeOffset.UtcNow, Score: 50m, Passed: true),
                new SubmissionVersionDto(Guid.NewGuid(), 2, SubmissionSourceDto.GuardianOnBehalf, "v2", Guid.NewGuid(), DateTimeOffset.UtcNow, Score: 66.67m, Passed: true),
            },
            Review: null);
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/students/{studentId}/submission")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(detail, _apiJsonOptions));

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        // Drive the click: list of submissions shows the student; click "View" to load the detail.
        cut.WaitForState(() => cut.FindAll("fluent-button").Any(b => b.TextContent.Trim() == "View"));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "View").Click();

        cut.WaitForAssertion(() =>
        {
            // WS-A3 — versions table shows the Score values formatted to "0.##".
            cut.Markup.Should().Contain("50", "the first version's score renders");
            cut.Markup.Should().Contain("66.67", "the second version's score renders (2dp rounding)");
            // The header always says "Passed"; scope to the v2 row's value cell so
            // the assertion proves the VALUE rendered, not the header (parent-fix
            // 2026-09-09: the escalation-authored Contain("Passed") was vacuously
            // true against the <th>Passed</th> header).
            cut.FindAll("table.recipients-table tbody tr")[1].QuerySelectorAll("td").ElementAt(4).TextContent.Trim()
                .Should().Be("Passed", "the Passed value label renders for the passing version");
            cut.Markup.Should().NotContain("Failed", "no version is marked Failed in this scenario");
        });
    }

    [TestMethod]
    public void Detail_VersionsTable_RendersFailedLabel_WhenPassedFalse()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);

        // A single non-passing version — the plan's coverage line
        // (renders "Passed"/"Failed") needs a positive "Failed" render.
        var studentId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var submissions = new[] {
            new SubmissionForReviewDto(submissionId, dto.Id, dto.Title, studentId, 1, ReviewStateDto.Pending, DateTimeOffset.UtcNow),
        };
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/submissions")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(submissions, _apiJsonOptions));

        SetupGetAssignment(dto);

        var detail = new SubmissionDetailDto(
            submissionId, dto.Id, studentId, 1, ReviewStateDto.Pending, DateTimeOffset.UtcNow,
            new[]
            {
                new SubmissionVersionDto(Guid.NewGuid(), 1, SubmissionSourceDto.Student, "v1", null, DateTimeOffset.UtcNow, Score: 40m, Passed: false),
            },
            Review: null);
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/students/{studentId}/submission")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(detail, _apiJsonOptions));

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForState(() => cut.FindAll("fluent-button").Any(b => b.TextContent.Trim() == "View"));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "View").Click();

        cut.WaitForAssertion(() =>
        {
            // Scope to the row's Passed value cell (td[4]) so the header's
            // <th>Passed</th> text cannot satisfy the assertion vacuously
            // (the parent-fix row-scoping pattern).
            var versionRow = cut.FindAll("table.recipients-table tbody tr").First();
            versionRow.QuerySelectorAll("td").ElementAt(4).TextContent.Trim()
                .Should().Be("Failed", "a version with Passed=false must render the 'Failed' label");
        });
    }

    [TestMethod]
    public void Detail_VersionsTable_RendersEmDashForNullScoreAndPassed()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);

        // WS-A3: register the specific /submissions backend BEFORE the generic
        // SetupGetAssignment fallback so the submission row actually renders.
        var studentId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var submissions = new[] {
            new SubmissionForReviewDto(submissionId, dto.Id, dto.Title, studentId, 1, ReviewStateDto.Pending, DateTimeOffset.UtcNow),
        };
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/submissions")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(submissions, _apiJsonOptions));

        SetupGetAssignment(dto);

        // TeacherGraded path: Score + Passed null → "—" fallback.
        var detail = new SubmissionDetailDto(
            submissionId, dto.Id, studentId, 1, ReviewStateDto.Pending, DateTimeOffset.UtcNow,
            new[]
            {
                new SubmissionVersionDto(Guid.NewGuid(), 1, SubmissionSourceDto.Student, "v1", null, DateTimeOffset.UtcNow, Score: null, Passed: null),
            },
            Review: null);
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/students/{studentId}/submission")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(detail, _apiJsonOptions));

        var cut = Render<DetailPage_Component>(parameters => parameters.Add(p => p.Id, dto.Id));

        cut.WaitForState(() => cut.FindAll("fluent-button").Any(b => b.TextContent.Trim() == "View"));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "View").Click();

        cut.WaitForAssertion(() =>
        {
            // Two em-dashes for the two null columns + one for the null Content (already shipped).
            cut.Markup.Should().Contain("\u2014", "the em-dash placeholder renders for null Score/Passed");
            // The Passed column HEADER always renders the word "Passed"; scope the
            // value assertions to the version row so the header text cannot collide
            // (parent-fix 2026-09-09: the escalation-authored NotContain("Passed")
            // deterministically failed against the <th>Passed</th> header).
            var versionRow = cut.FindAll("table.recipients-table tbody tr").Single();
            var cells = versionRow.QuerySelectorAll("td").ToArray();
            cells[3].TextContent.Trim().Should().Be("\u2014", "null Score renders the em-dash placeholder");
            cells[4].TextContent.Trim().Should().Be("\u2014", "null Passed renders the em-dash placeholder");
            cut.Markup.Should().NotContain("Failed", "no Failed value label when Passed is null");
        });
    }
}
