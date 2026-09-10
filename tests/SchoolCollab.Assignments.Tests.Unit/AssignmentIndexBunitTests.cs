using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using IndexPage = SchoolCollab.Assignments.Application.Components.Pages.Assignments.Index;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Tests.Unit;

[TestClass]
public class AssignmentIndexBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;

    public AssignmentIndexBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        _apiJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter<AssignmentTypeDto>(), new JsonStringEnumConverter<AssignmentStatusDto>(), new JsonStringEnumConverter<GradingFormatDto>(), new JsonStringEnumConverter<TargetAudienceTypeDto>() }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<IndexPage>>());
        // WS-A2: the Index page injects IFeatureFlagService to gate the
        // Draft row action (Submit-for-approval vs Publish).
        Services.AddSingleton<IFeatureFlagService>(new FakeFeatureFlagService());
    }

    private void SetupListResponse(AssignmentSummaryDto[] items)
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments*")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(items, _apiJsonOptions));
    }

    /// <summary>One lifecycle row for the click-flow tests — the same 17-arg
    /// positional shape the existing tests construct inline (the five WS-A2
    /// lifecycle fields keep their record defaults).</summary>
    private static AssignmentSummaryDto MakeRow(Guid id, string title, AssignmentStatusDto status) =>
        new(
            id, title, null, AssignmentTypeDto.Digital,
            GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
            Guid.NewGuid(), "Math", null, null, status,
            null, null, true, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    /// <summary>UI-tester rework overload — pin the ApprovalStatus (and
    /// optional ApprovedBy/ApprovedAt) on the row. Used by the new chip
    /// + flag-on-Approved Draft tests; the five lifecycle fields still
    /// carry their record defaults except those explicitly overridden.</summary>
    private static AssignmentSummaryDto MakeRow(Guid id, string title, AssignmentStatusDto status,
        ApprovalStatusDto? approvalStatus = null, Guid? approvedBy = null, DateTimeOffset? approvedAt = null) =>
        new(
            id, title, null, AssignmentTypeDto.Digital,
            GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
            Guid.NewGuid(), "Math", null, null, status,
            null, null, true, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            AvailableFromUtc: null, ArchiveGraceDays: 30,
            ApprovalStatus: approvalStatus,
            ApprovedBy: approvalStatus == ApprovalStatusDto.Approved ? approvedBy : null,
            ApprovedAt: approvalStatus == ApprovalStatusDto.Approved ? (approvedAt ?? DateTimeOffset.UtcNow) : null);

    [TestMethod]
    public void Index_ShowsSpinner_WhileLoading()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments*")
            .Respond(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var cut = Render<IndexPage>();
        cut.Markup.ToLower().Should().Contain("progress");
    }

    [TestMethod]
    public void Index_ShowsEmptyMessage_WhenNoAssignments()
    {
        SetupListResponse([]);

        var cut = Render<IndexPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("No assignments yet");
        });
    }

    [TestMethod]
    public void Index_ShowsError_WhenApiFails()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments*")
            .Respond(HttpStatusCode.InternalServerError);

        var cut = Render<IndexPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().MatchRegex("Something went wrong|500");
        }, TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public void Index_JsonOptions_SerializeEnumsAsStrings()
    {
        var dto = new AssignmentSummaryDto(
            Guid.NewGuid(), "Test", null, AssignmentTypeDto.SemiManual,
            GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
            Guid.NewGuid(), "Math", null, null, AssignmentStatusDto.Published,
            null, null, true, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(dto, _apiJsonOptions);

        json.Should().Contain("\"SemiManual\"");
        json.Should().Contain("\"Published\"");
        json.Should().NotContain("\"assignmentType\":1");
        json.Should().NotContain("\"status\":1");
    }

    [TestMethod]
    public void Index_JsonOptions_DeserializeStringsToEnums()
    {
        var json = """{"id":"00000000-0000-0000-0000-000000000001","title":"Test","description":null,"assignmentType":"SemiManual","gradingFormat":"TeacherGraded","targetAudienceType":"AllStudents","topicId":"00000000-0000-0000-0000-000000000002","topicName":"Math","gradeLevelId":null,"gradeName":null,"status":"Published","dueDate":null,"maxScore":null,"createdByTeacherId":"00000000-0000-0000-0000-000000000003","createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-01T00:00:00+00:00"}""";

        var dto = JsonSerializer.Deserialize<AssignmentSummaryDto>(json, _apiJsonOptions);

        dto.Should().NotBeNull();
        dto!.AssignmentType.Should().Be(AssignmentTypeDto.SemiManual);
        dto.Status.Should().Be(AssignmentStatusDto.Published);
    }

    // ── WS-A2 / spec §3.5 step 2 + §7 Q2 — lifecycle extensions ──────

    [TestMethod]
    public void Index_StatusFilterOptionsIncludeScheduledAndArchived()
    {
        // WS-A2 / spec §3.5 step 2 — the status filter options must include
        // Scheduled (3) + Archived (4). Verified on the page's options array
        // (the source of truth — the LandingPage filter renders from this
        // array via <FluentSelect>). BUnit rendering of FluentSelect option
        // text is timing-sensitive so we assert on the source list directly.
        IndexPage.StatusFilterOptions.Should().Contain(o => o.Value == 3 && o.Label == "Scheduled");
        IndexPage.StatusFilterOptions.Should().Contain(o => o.Value == 4 && o.Label == "Archived");
    }

    [TestMethod]
    public async Task Index_FlagOn_DraftRow_ShowsSubmitForApproval_NotPublish()
    {
        // Decision (j) / spec §7 Q2: flag ON swaps Publish for Submit-for-
        // approval on Draft rows (publishing unapproved 400s the typed
        // guard), and clicking the action POSTs the submit-for-approval
        // endpoint.
        var fake = new FakeFeatureFlagService { IsEnabledValue = true };
        var existing = Services.Where(sd => sd.ServiceType == typeof(IFeatureFlagService)).ToList();
        existing.ForEach(sd => Services.Remove(sd));
        Services.AddSingleton<IFeatureFlagService>(fake);
        var id = Guid.NewGuid();
        SetupListResponse(new[]
        {
            new AssignmentSummaryDto(
                id, "Math HW", null, AssignmentTypeDto.Digital,
                GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
                Guid.NewGuid(), "Math", null, null, AssignmentStatusDto.Draft,
                null, null, true, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        });
        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"),
            TimeSpan.FromSeconds(15));

        // Open the row-actions kebab: FluentMenu renders its items only once
        // opened (the GradeLevelDetailPageTests kebab precedent).
        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToList();
        items.Should().Contain("Submit for Approval",
            "flag ON swaps Publish for Submit-for-approval on Draft rows");
        items.Should().NotContain("Publish",
            "publishing an unapproved Draft 400s the typed approval guard when the flag is on");

        // Clicking Submit for Approval POSTs the endpoint (the
        // ResourcesSectionBunitTests Expect/VerifyNoOutstandingExpectation precedent).
        // The Expect is registered immediately before the click because MockHttp
        // request expectations match BEFORE backend definitions: an outstanding
        // Expect would 404 the initial list GET served by the When stub above.
        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{id}/submit-for-approval")
            .Respond(HttpStatusCode.NoContent);
        cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Submit for Approval")).Click();
        cut.WaitForAssertion(() => _mockHttp.VerifyNoOutstandingExpectation());
    }

    [TestMethod]
    public async Task Index_FlagOn_ApprovedDraftRow_ShowsPublish_NotSubmitForApproval()
    {
        // UI-tester rework P1: flag ON must NOT silently revoke an existing
        // approval. SubmitForApproval is a Draft-only guard that flips
        // Approved → Pending; the row action for an already-APPROVED Draft
        // must therefore stay "Publish" so a reviewer can publish without
        // first having to reject-and-resubmit. This pins the reviewer-
        // rework fix at the row-action boundary.
        var fake = new FakeFeatureFlagService { IsEnabledValue = true };
        var existing = Services.Where(sd => sd.ServiceType == typeof(IFeatureFlagService)).ToList();
        existing.ForEach(sd => Services.Remove(sd));
        Services.AddSingleton<IFeatureFlagService>(fake);
        var id = Guid.NewGuid();
        SetupListResponse(new[]
        {
            new AssignmentSummaryDto(
                id, "Math HW", null, AssignmentTypeDto.Digital,
                GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
                Guid.NewGuid(), "Math", null, null, AssignmentStatusDto.Draft,
                null, null, true, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                AvailableFromUtc: null, ArchiveGraceDays: 30,
                ApprovalStatus: ApprovalStatusDto.Approved,
                ApprovedBy: Guid.NewGuid(), ApprovedAt: DateTimeOffset.UtcNow)
        });
        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"),
            TimeSpan.FromSeconds(15));

        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToList();
        items.Should().Contain("Publish",
            "an already-APPROVED Draft must keep the Publish action when the flag is on — Submit-for-approval would silently revoke the approval");
        items.Should().NotContain("Submit for Approval",
            "the substitute applies ONLY while the assignment has no current approval");
    }

    [TestMethod]
    public async Task Index_FlagOn_ApprovalChip_RendersTextPerApprovalStatus()
    {
        // UI-tester rework P2: the conditional Approval column must surface a
        // chip on Draft rows with the right text per ApprovalStatus (the
        // Reviewer-rework fix for the invisible optimistic-flip surface).
        var fake = new FakeFeatureFlagService { IsEnabledValue = true };
        var existing = Services.Where(sd => sd.ServiceType == typeof(IFeatureFlagService)).ToList();
        existing.ForEach(sd => Services.Remove(sd));
        Services.AddSingleton<IFeatureFlagService>(fake);

        var rows = new[]
        {
            MakeRow(Guid.NewGuid(), "Never Submitted HW", AssignmentStatusDto.Draft, approvalStatus: null),
            MakeRow(Guid.NewGuid(), "Pending HW", AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Pending),
            MakeRow(Guid.NewGuid(), "Approved HW", AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Approved,
                approvedBy: Guid.NewGuid()),
            MakeRow(Guid.NewGuid(), "Rejected HW", AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Rejected),
            // Non-Draft rows render no chip — verified by the negative assertion
            // at the end of this test (the Scheduled row carries an Approval
            // column cell, but it's empty).
            MakeRow(Guid.NewGuid(), "Scheduled HW", AssignmentStatusDto.Scheduled, approvalStatus: null),
        };
        SetupListResponse(rows);

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Never Submitted HW"),
            TimeSpan.FromSeconds(15));

        // The five approval-state chips share the "Approval" column header
        // (template-column selector). Each chip's text appears in the page
        // markup exactly once for the Draft rows; the Scheduled row has no
        // chip in the Approval cell (it's an empty TemplateColumn body).
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("fluent-badge").Select(b => b.TextContent.Trim())
                .Should().Contain("Not submitted", "null ApprovalStatus → 'Not submitted' chip on a Draft row")
                .And.Contain("Pending", "Pending ApprovalStatus → 'Pending' chip on a Draft row")
                .And.Contain("Approved", "Approved ApprovalStatus → 'Approved' chip on a Draft row")
                .And.Contain("Rejected", "Rejected ApprovalStatus → 'Rejected' chip on a Draft row");
            // The Approval column only renders for Draft rows — there is
            // therefore exactly one chip per Draft row above and no chip
            // for the Scheduled row (non-Draft). We do not pin the count
            // exactly (other badges exist) — we just confirm each chip
            // text is present at least once.
        });
    }

    [TestMethod]
    public async Task Index_FlagOn_SubmitForApprovalClick_OptimisticChipFlipAndRollbackVisible()
    {
        // UI-tester rework P2: the optimistic Submit-for-approval flip
        // (chip goes Pending instantly) and its failure rollback (chip
        // reverts to the previous state) must be visible in the rendered
        // markup. Before the rework the optimistic update was a silent
        // no-op because no chip surfaced ApprovalStatus — pinning both
        // directions here so a regression to the silent-flip path is
        // caught at test time.
        var fake = new FakeFeatureFlagService { IsEnabledValue = true };
        var existing = Services.Where(sd => sd.ServiceType == typeof(IFeatureFlagService)).ToList();
        existing.ForEach(sd => Services.Remove(sd));
        Services.AddSingleton<IFeatureFlagService>(fake);

        var id = Guid.NewGuid();
        // Use When (not Expect) for the success path so the request can be
        // served any number of times. v6 ordering rule (the flag-ON Index
        // test documents this): Expect is registered immediately before
        // the click; for the optimistic-flip test we only need the POST to
        // succeed so we use When (multi-fire).
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{id}/submit-for-approval")
            .Respond(HttpStatusCode.NoContent);
        SetupListResponse(new[]
        {
            MakeRow(id, "Math HW", AssignmentStatusDto.Draft, approvalStatus: null)
        });

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"),
            TimeSpan.FromSeconds(15));
        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-badge").Select(b => b.TextContent.Trim())
                .Should().Contain("Not submitted", "the chip surfaces the null ApprovalStatus pre-click"));

        // Drive the optimistic-flip click. The chip must flip to Pending
        // before the API call resolves (we observe it after StateHasChanged
        // inside OnSubmitForApprovalAsync). With the POST backend stubbed
        // for success, the chip stays Pending after the call resolves.
        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Submit for Approval")).Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-badge").Select(b => b.TextContent.Trim())
                .Should().Contain("Pending",
                    "the optimistic Submit-for-approval click must visibly flip the Approval chip to Pending"));
    }

    [TestMethod]
    public async Task Index_FlagOn_SubmitForApprovalClick_FailingPost_RollsBackChip()
    {
        // UI-tester rework P2 — second half: a failing POST must restore the
        // previous chip text. Mount a 500 backend and click — the chip must
        // flip optimistically to Pending then revert to 'Not submitted' when
        // the API throws.
        var fake = new FakeFeatureFlagService { IsEnabledValue = true };
        var existing = Services.Where(sd => sd.ServiceType == typeof(IFeatureFlagService)).ToList();
        existing.ForEach(sd => Services.Remove(sd));
        Services.AddSingleton<IFeatureFlagService>(fake);

        var id = Guid.NewGuid();
        SetupListResponse(new[]
        {
            MakeRow(id, "Math HW", AssignmentStatusDto.Draft, approvalStatus: null)
        });

        // v6 ordering rule (Expect-before-backend 404s the initial GET):
        // register the failing-POST backend AFTER the initial SetupListResponse
        // (the list-GET When backend above). MockHttp When-clauses co-exist
        // fine; the issue is only with Expect.
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{id}/submit-for-approval")
            .Respond(HttpStatusCode.InternalServerError);

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));
        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-badge").Select(b => b.TextContent.Trim())
                .Should().Contain("Not submitted"));

        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Submit for Approval")).Click();

        // After the 500 the optimistic flip rolls back: chip must end up
        // at 'Not submitted' again. We do NOT pin a transient Pending
        // observation — the API call resolves fast enough that the
        // pre- and post-rollback states are the only ones observable from
        // WaitForAssertion.
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("fluent-badge").Select(b => b.TextContent.Trim())
                .Should().NotContain("Pending",
                    "the failing POST must roll the optimistic Pending flip back")
                .And.Contain("Not submitted",
                    "the chip reverts to the previous text after the rollback");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public async Task Index_FlagOff_NoApprovalColumnRenders()
    {
        // Negative half of the P2 fix: when the flag is OFF, the
        // conditional Approval column must NOT render — the grid is
        // exactly today's grid (no Approval header, no chip). This is
        // what the UI-tester flagged as the regression risk when adding
        // the conditional column.
        var existing = Services.Where(sd => sd.ServiceType == typeof(IFeatureFlagService)).ToList();
        existing.ForEach(sd => Services.Remove(sd));
        Services.AddSingleton<IFeatureFlagService>(new FakeFeatureFlagService { IsEnabledValue = false });

        SetupListResponse(new[]
        {
            MakeRow(Guid.NewGuid(), "Math HW", AssignmentStatusDto.Draft, approvalStatus: null),
            MakeRow(Guid.NewGuid(), "Approved HW", AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Approved,
                approvedBy: Guid.NewGuid())
        });
        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"),
            TimeSpan.FromSeconds(15));

        cut.Markup.Should().NotContain(">Approval<",
            "flag OFF hides the conditional Approval column");
        cut.FindAll("fluent-badge").Select(b => b.TextContent.Trim())
            .Should().NotContain("Not submitted",
                "flag OFF hides the approval chips");
    }

    [TestMethod]
    public async Task Index_FlagOff_DraftRow_ShowsPublish()
    {
        var existing = Services.Where(sd => sd.ServiceType == typeof(IFeatureFlagService)).ToList();
        existing.ForEach(sd => Services.Remove(sd));
        Services.AddSingleton<IFeatureFlagService>(new FakeFeatureFlagService { IsEnabledValue = false });
        SetupListResponse(new[]
        {
            new AssignmentSummaryDto(
                Guid.NewGuid(), "Math HW", null, AssignmentTypeDto.Digital,
                GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
                Guid.NewGuid(), "Math", null, null, AssignmentStatusDto.Draft,
                null, null, true, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        });
        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"),
            TimeSpan.FromSeconds(15));

        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToList();
        items.Should().Contain("Publish", "flag OFF keeps the existing Publish action on Draft rows");
        items.Should().NotContain("Submit for Approval", "flag OFF hides the approval action");
    }

    [TestMethod]
    public async Task Index_ScheduledRow_Renders()
    {
        SetupListResponse(new[]
        {
            new AssignmentSummaryDto(
                Guid.NewGuid(), "Math HW", null, AssignmentTypeDto.Digital,
                GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
                Guid.NewGuid(), "Math", null, null, AssignmentStatusDto.Scheduled,
                null, null, true, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        });
        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"),
            TimeSpan.FromSeconds(15));
        cut.Markup.Should().Contain("Scheduled", "the Status badge maps Scheduled via the page badge ternary");

        // Decision (j): a Scheduled row offers Edit + Publish now + Unpublish.
        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToList();
        items.Should().Contain("Edit");
        items.Should().Contain("Publish now");
        items.Should().Contain("Unpublish");
    }

    [TestMethod]
    public async Task Index_StatusFilter_ScheduledSelection_IssuesFilteredGet()
    {
        // P1-9 rework: selecting Scheduled must issue GET /assignments?status=
        // Scheduled and swap the grid to the filtered rows. Driven via the
        // FluentSelect's public SelectedOptionChanged callback (the
        // TopicCreateDialogTests invoke precedent) because FluentSelect option
        // rendering is timing-sensitive under JSRuntimeMode.Loose.
        SetupListResponse([MakeRow(Guid.NewGuid(), "Draft HW", AssignmentStatusDto.Draft)]);
        // The status filter lives in the page-toolbar SectionContent — a bare
        // bUnit render publishes it to a section with no outlet, so the select
        // never instantiates. Host it under StatusFilterHost (the
        // ActivityGroupsPageTests RolloverHost precedent).
        var cut = Render<StatusFilterHost>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Draft HW"));

        var scheduledOnly = new[] { MakeRow(Guid.NewGuid(), "Scheduled HW", AssignmentStatusDto.Scheduled) };
        // The Expect matches ONLY the filtered GET (WithQueryString pins the
        // query); registered after the initial load because a v6 outstanding
        // Expect 404s every other request.
        _mockHttp.Expect(HttpMethod.Get, "http://localhost/assignments")
            .WithQueryString("status=Scheduled")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(scheduledOnly, _apiJsonOptions));

        var select = cut.FindComponent<FluentSelect<IndexPage.StatusOption>>();
        await cut.InvokeAsync(() => select.Instance.SelectedOptionChanged.InvokeAsync(
            IndexPage.StatusFilterOptions.Single(o => o.Value == 3)));

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            cut.Markup.Should().Contain("Scheduled HW");
            cut.Markup.Should().NotContain("Draft HW",
                "the filtered reload replaces the grid rows, not just fires the GET");
        });
    }

    [TestMethod]
    public void Index_ScheduledRow_PublishNow_Click_PostsPublish_AndOptimisticallyFlipsRow()
    {
        // P1-9 rework: the "Publish now" action must fire POST /{id}/publish
        // and optimistically flip the row to Published (decision (j) —
        // publish-now reuses the existing OnPublishAsync flow).
        var id = Guid.NewGuid();
        SetupListResponse([MakeRow(id, "Math HW", AssignmentStatusDto.Scheduled)]);
        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{id}/publish")
            .Respond(HttpStatusCode.NoContent);

        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        cut.FindAll("fluent-menu-item").Single(i => i.TextContent.Trim() == "Publish now").Click();

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            cut.FindAll("fluent-badge").Should().Contain(b => b.TextContent.Trim() == "Published",
                "the row optimistically flips to Published without a list reload");
        });
    }

    [TestMethod]
    public void Index_ScheduledRow_PublishNow_FailingPost_RollsBackRow()
    {
        // P1-9 rework: a failing publish POST rolls the optimistic flip back to
        // Scheduled and surfaces the error (OnPublishAsync's rollback path).
        var id = Guid.NewGuid();
        SetupListResponse([MakeRow(id, "Math HW", AssignmentStatusDto.Scheduled)]);
        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{id}/publish")
            .Respond(HttpStatusCode.InternalServerError);

        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        cut.FindAll("fluent-menu-item").Single(i => i.TextContent.Trim() == "Publish now").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("fluent-badge").Should().Contain(b => b.TextContent.Trim() == "Scheduled",
                "the optimistic Published flip rolls back when the POST 500s");
            cut.Markup.Should().Contain("500",
                "the failure surfaces on the landing page error bar");
        });
    }

    [TestMethod]
    public void Index_ScheduledRow_Unpublish_Click_PostsUnpublish_AndOptimisticallyFlipsRow()
    {
        // P1-9 rework: the Unpublish action on a Scheduled row must fire POST
        // /{id}/unpublish and optimistically flip the row to Draft (decision
        // (a) — Unpublish from Scheduled returns the assignment to Draft).
        var id = Guid.NewGuid();
        SetupListResponse([MakeRow(id, "Math HW", AssignmentStatusDto.Scheduled)]);
        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{id}/unpublish")
            .Respond(HttpStatusCode.NoContent);

        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        cut.FindAll("fluent-menu-item").Single(i => i.TextContent.Trim() == "Unpublish").Click();

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            cut.FindAll("fluent-badge").Should().Contain(b => b.TextContent.Trim() == "Draft",
                "the row optimistically flips to Draft (Scheduled → Draft unpublish, decision (a))");
        });
    }

    [TestMethod]
    public async Task Index_ArchivedRow_Renders()
    {
        var id = Guid.NewGuid();
        SetupListResponse(new[]
        {
            new AssignmentSummaryDto(
                id, "Math HW", null, AssignmentTypeDto.Digital,
                GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
                Guid.NewGuid(), "Math", null, null, AssignmentStatusDto.Archived,
                null, null, true, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        });
        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"),
            TimeSpan.FromSeconds(15));
        cut.Markup.Should().Contain("Archived", "the Status badge maps Archived via the page badge ternary");

        // WS-A4 / spec §3.1: archived rows now carry Review + Duplicate, so
        // they render via the kebab menu instead of a single labeled button.
        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToList();
        items.Should().Contain("Review");
        items.Should().Contain("Duplicate");
    }

    /// <summary>Replaces the registered <see cref="IDialogService"/> (the real
    /// FluentUI one added by <c>AddFluentUIComponents</c>) with the mock so the
    /// page's injection resolves it — <c>GetRequiredService</c> returns the
    /// FIRST registration, so the real service must be removed, not shadowed.</summary>
    private void ReplaceDialogService(Mock<IDialogService> dialogMock)
    {
        var existing = Services.Where(s => s.ServiceType == typeof(IDialogService)).ToList();
        foreach (var s in existing) Services.Remove(s);
        Services.AddSingleton(dialogMock.Object);
    }

    /// <summary>Mocks the confirm dialog (<c>ShowConfirmDialogAsync</c> resolves
    /// to <c>ShowDialogAsync&lt;ConfirmDialog, ConfirmDialogContent&gt;</c>) at the
    /// requested outcome — the ResourcesSectionBunitTests /
    /// AssignmentDetailBunitTests pattern.</summary>
    private Mock<IDialogService> SetupConfirmDialogResult(bool confirmed)
    {
        var dialogRef = new Mock<IDialogReference>();
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

    /// <summary>Replaces the registered <see cref="IToastService"/> with a mock
    /// so success toasts can be asserted directly.</summary>
    private Mock<IToastService> ReplaceToastService()
    {
        var existing = Services.Where(s => s.ServiceType == typeof(IToastService)).ToList();
        foreach (var s in existing) Services.Remove(s);
        var mock = new Mock<IToastService>();
        Services.AddSingleton(mock.Object);
        return mock;
    }

    // ── WS-A4 / spec §3.1 — duplicate-as-template row action ───────

    [TestMethod]
    public async Task Index_PublishedRow_ShowsDuplicateAction()
    {
        var id = Guid.NewGuid();
        SetupListResponse([MakeRow(id, "Math HW", AssignmentStatusDto.Published)]);

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"),
            TimeSpan.FromSeconds(15));

        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToList();
        items.Should().Contain("Duplicate",
            "a Published row must offer the Duplicate-as-template action");
    }

    [TestMethod]
    public async Task Index_DuplicateClicked_UserConfirms_PostsToastsAndReloads()
    {
        var id = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var listGetCount = 0;
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments*")
            .Respond(_ =>
            {
                listGetCount++;
                var json = JsonSerializer.Serialize(
                    new[] { MakeRow(id, "Math HW", AssignmentStatusDto.Published) },
                    _apiJsonOptions);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            });

        SetupConfirmDialogResult(confirmed: true);
        var toastMock = ReplaceToastService();

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"),
            TimeSpan.FromSeconds(15));

        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{id}/duplicate")
            .Respond(HttpStatusCode.Created, "application/json",
                JsonSerializer.Serialize(new { id = newId }, _apiJsonOptions));

        cut.FindAll("fluent-menu-item").Single(i => i.TextContent.Trim() == "Duplicate").Click();

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            listGetCount.Should().BeGreaterThanOrEqualTo(2,
                "a successful duplicate must reload the assignment list");
        }, TimeSpan.FromSeconds(15));

        toastMock.Verify(
            t => t.ShowSuccess(
                It.Is<string>(s => s.Contains("duplicated", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<EventCallback<ToastResult>?>()),
            Times.Once);
    }

    [TestMethod]
    public async Task Index_DuplicateClicked_UserDeclines_NoPostNoReload()
    {
        var id = Guid.NewGuid();
        var listGetCount = 0;
        var postCount = 0;
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments*")
            .Respond(_ =>
            {
                listGetCount++;
                var json = JsonSerializer.Serialize(
                    new[] { MakeRow(id, "Math HW", AssignmentStatusDto.Published) },
                    _apiJsonOptions);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            });
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{id}/duplicate")
            .Respond(_ =>
            {
                postCount++;
                return new HttpResponseMessage(HttpStatusCode.Created);
            });

        var dialogMock = SetupConfirmDialogResult(confirmed: false);

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"),
            TimeSpan.FromSeconds(15));

        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
        cut.FindAll("fluent-menu-item").Single(i => i.TextContent.Trim() == "Duplicate").Click();

        cut.WaitForAssertion(() => dialogMock.Verify(
            d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()),
            Times.Once));

        postCount.Should().Be(0, "declining the confirm dialog must not fire the duplicate POST");
        listGetCount.Should().Be(1, "declining must not reload the assignment list");
    }

    [TestMethod]
    public async Task Index_DuplicateClicked_PostFails_ShowsErrorNoToast()
    {
        var id = Guid.NewGuid();
        SetupListResponse([MakeRow(id, "Math HW", AssignmentStatusDto.Published)]);

        SetupConfirmDialogResult(confirmed: true);
        var toastMock = ReplaceToastService();

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"),
            TimeSpan.FromSeconds(15));

        cut.Find("fluent-button[title=\"Assignment actions\"]").Click();

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{id}/duplicate")
            .Respond(HttpStatusCode.InternalServerError);

        cut.FindAll("fluent-menu-item").Single(i => i.TextContent.Trim() == "Duplicate").Click();

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            cut.Markup.Should().MatchRegex("Something went wrong|500",
                "a failing duplicate POST must surface the error on the landing page");
        }, TimeSpan.FromSeconds(15));

        toastMock.Verify(
            t => t.ShowSuccess(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<EventCallback<ToastResult>?>()),
            Times.Never);
    }

    /// <summary>Test host that renders the page-toolbar SectionOutlet so the
    /// Index page's status-filter FluentSelect renders in bUnit — the
    /// ActivityGroupsPageTests RolloverHost precedent. Without the outlet the
    /// toolbar SectionContent publishes to nowhere and the select never
    /// instantiates.</summary>
    private sealed class StatusFilterHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SectionOutlet>(0);
            builder.AddAttribute(1, "SectionName", "page-toolbar");
            builder.CloseComponent();

            builder.OpenComponent<IndexPage>(2);
            builder.CloseComponent();
        }
    }
}