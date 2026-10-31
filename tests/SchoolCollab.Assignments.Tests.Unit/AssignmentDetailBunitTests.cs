using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Services;
using DetailPage = SchoolCollab.Assignments.Application.Components.Pages.Assignments.Detail;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>assignment-view-page</c> — the rebuilt view page (spec
/// assignment-detail-view-modernization §4.1, §4.8–§4.10; round plan P5, P5b, P7, P8).
/// <para>Pins the external behaviour the round's acceptance rests on: ONE shell-owned action-bar
/// heading and ZERO tabs; both regions present; the status-driven primary (Publish / Unpublish /
/// Archive / Open progress / none) with a visible reason wherever it is disabled (R1/P5b/AC4b/AC4c);
/// the page menu's lifecycle verbs; the approval block's four decisions and its read-only approved
/// state; the rail's Content &amp; Resources / Rules / Approval panels; the audience table's verdict set
/// in both states; the pager absent when the set fits and the cap stated where the pager is
/// (Q162/Q146).</para>
/// </summary>
[TestClass]
public class AssignmentDetailBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;

    /// <summary>The circuit principal the approval verbs read their acting teacher from. No
    /// <c>teacher_id</c> claim by default, so the bodies carry the server's <c>Guid.Empty</c>
    /// placeholder — the same posture the reference approval tests pinned.</summary>
    private readonly StubAuthStateProvider _authStateProvider = new();

    /// <summary>GET-hit counter for the assignment endpoint: the approval verbs reload the summary
    /// after a successful POST, and a count of 2+ is the "page refreshed" observable.</summary>
    private int _assignmentGetCount;

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
                new JsonStringEnumConverter<ReviewStateDto>(),
                // The Questions block's rows travel as the authoring child read's own DTO.
                new JsonStringEnumConverter<QuestionTypeDto>(),
                // The audience read's own enums travel as names on the real host and on the client.
                new JsonStringEnumConverter<ContactOwnerTypeDto>(),
                new JsonStringEnumConverter<ContactChannelDto>(),
                new JsonStringEnumConverter<GuardianRoleDto>(),
                new JsonStringEnumConverter<AudienceDeliveryStateDto>()
            }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<DetailPage>>());

        // The Approval block's verbs read the acting teacher off the circuit principal.
        Services.AddSingleton<AuthenticationStateProvider>(_authStateProvider);

        // The Details block EMBEDS the authoring surface's read-only body (R7/D2), so the page's render
        // tree now needs that component's own dependencies. They are registered here, at the page
        // level, because the page is the thing under test — its child's backends are stubs.
        Services.AddSingleton<StudentsApiClient>();
        Services.AddSingleton<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<StudentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<AssignmentAuthoring>>());
        Services.AddSingleton<IFeatureFlagService>(new FakeFeatureFlagService());
    }

    private static AssignmentSummaryDto MakeDto(
        AssignmentStatusDto status,
        ApprovalStatusDto? approvalStatus = null,
        DateTimeOffset? dueDate = null,
        bool requiresSignature = false,
        DateTimeOffset? publishedAt = null,
        bool requiresApproval = false,
        string topicName = "Biology",
        string title = "Photosynthesis lab report")
    {
        return new AssignmentSummaryDto(
            Id: Guid.NewGuid(),
            Title: title,
            Description: null,
            AssignmentType: AssignmentTypeDto.Digital,
            GradingFormat: GradingFormatDto.TeacherGraded,
            TargetAudienceType: TargetAudienceTypeDto.AllStudents,
            TopicId: Guid.NewGuid(),
            TopicName: topicName,
            Status: status,
            DueDate: dueDate,
            MaxScore: 20,
            MandatoryReview: false,
            CreatedByTeacherId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            AvailableFromUtc: null,
            ArchiveGraceDays: 30,
            ApprovalStatus: approvalStatus,
            RequiresSignature: requiresSignature,
            RequiresApproval: requiresApproval,
            // "Has ever been published" is the page's own state selector: it picks the log-backed
            // audience read, the submission counts and the Status panel's published wording.
            PublishedAt: publishedAt ?? (status is AssignmentStatusDto.Published
                or AssignmentStatusDto.Scheduled
                or AssignmentStatusDto.Closed
                or AssignmentStatusDto.Archived
                    ? DateTimeOffset.UtcNow.AddDays(-1)
                    : null));
    }

    private static AudienceStudentDto UnreachedStudent(string name) =>
        new(Guid.NewGuid(), name, false, []);

    private static AudienceContactDto Contact(
        AudienceDeliveryStateDto state,
        ContactChannelDto channel = ContactChannelDto.Email,
        GuardianRoleDto? role = GuardianRoleDto.Primary,
        DateTimeOffset? sentAt = null,
        DateTimeOffset? openedAt = null,
        string? failureReason = null,
        bool retryScheduled = false) =>
        new(ContactOwnerTypeDto.Guardian, channel, role, SubscriptionActive: true, NotifyOnBroadcast: true,
            state, sentAt, openedAt, failureReason, retryScheduled, TemplateId: null);

    private static AssignmentAudienceDto Audience(IEnumerable<AudienceStudentDto> rows, bool truncated = false) =>
        new(rows.ToList(), truncated, Degraded: false);

    private static AssignmentQuestionReadDto Question(
        int order,
        string text,
        QuestionTypeDto type = QuestionTypeDto.MultipleChoice,
        params (string Text, bool Correct)[] options) =>
        new(Guid.NewGuid(), text, type, order, null,
            options.Select(option => new AssignmentQuestionOptionReadDto(Guid.NewGuid(), option.Text, option.Correct))
                .ToList());

    /// <summary>The resolved policy the fixture answers with unless a test supplies its own: every
    /// optional field unset, so a row's "built-in" wording is what the panel has to state.</summary>
    private static readonly EffectiveAssignmentPolicy DefaultPolicy = new(
        SignatureRequirement: SignatureRequirementMode.Disabled,
        RequiresApprovalBeforePublish: false,
        MaxPrimaryContacts: null,
        MaxCopyContacts: null,
        MandatoryReview: null,
        ArchiveGraceDays: null,
        ApprovalWindowDays: null,
        SignatureRequirementFromOverride: false,
        RequiresApprovalBeforePublishFromOverride: false,
        MaxPrimaryContactsFromOverride: false,
        MaxCopyContactsFromOverride: false,
        MandatoryReviewFromOverride: false,
        ArchiveGraceDaysFromOverride: false,
        ApprovalWindowDaysFromOverride: false);

    private static AssignmentAttachmentReadDto Attachment(
        string fileName,
        string contentType,
        AttachmentExtractionStatusDto extractionStatus = AttachmentExtractionStatusDto.Succeeded,
        string? extractionError = null) =>
        new(Guid.NewGuid(), fileName, contentType, 1024, $"attachments/{fileName}", extractionStatus,
            extractionStatus == AttachmentExtractionStatusDto.Succeeded ? "extracted" : null,
            extractionStatus == AttachmentExtractionStatusDto.Succeeded ? DateTimeOffset.UtcNow : null,
            extractionError);

    private static ResourceDto Resource(string? displayName, string? url) =>
        new(Guid.NewGuid(), Guid.Empty, ResourceKindDto.Url, url, null, displayName, IncludedInGeneration: true);

    private void SetupGetAssignment(
        AssignmentSummaryDto dto,
        AssignmentAudienceDto? audience = null,
        SubmissionForReviewDto[]? submissions = null,
        SignOffStatusDto[]? signOff = null,
        IReadOnlyList<AssignmentQuestionReadDto>? questions = null,
        bool childrenReadFails = false,
        EffectiveAssignmentPolicy? policy = null,
        bool policyReadFails = false,
        IReadOnlyList<AssignmentAttachmentReadDto>? attachments = null,
        IReadOnlyList<ResourceDto>? resources = null,
        bool audienceReadFails = false)
    {
        // The Rules panel's read. Registered before the detail read (MockHttp v6 first-match ordering
        // is registration order): the id route is a literal guid and cannot collide, but the wildcard
        // must not be reached through a broader handler either.
        if (policyReadFails)
        {
            _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/effective-policy*")
                .Respond(HttpStatusCode.InternalServerError);
        }
        else
        {
            _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/effective-policy*")
                .Respond(HttpStatusCode.OK, "application/json",
                    JsonSerializer.Serialize(policy ?? DefaultPolicy, _apiJsonOptions));
        }

        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}")
            .Respond(_ =>
            {
                _assignmentGetCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(dto, _apiJsonOptions),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            });

        // ONE child read feeds the Questions table and the rail's Content & Resources panel (P7).
        if (childrenReadFails)
        {
            _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/authoring")
                .Respond(HttpStatusCode.InternalServerError);
        }
        else
        {
            _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/authoring")
                .Respond(HttpStatusCode.OK, "application/json",
                    JsonSerializer.Serialize(
                        new AssignmentAuthoringChildrenDto(
                            dto.Id, questions ?? [], attachments ?? [], resources ?? [], []),
                        _apiJsonOptions));
        }

        // The published read is log-backed; the draft read resolves the persisted targets (P2/P2b).
        var audiencePath = dto.PublishedAt is null ? "audience/preview" : "audience";
        if (audienceReadFails)
        {
            _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/{audiencePath}")
                .Respond(HttpStatusCode.InternalServerError);
        }
        else
        {
            _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/{audiencePath}")
                .Respond(HttpStatusCode.OK, "application/json",
                    JsonSerializer.Serialize(audience ?? Audience([]), _apiJsonOptions));
        }

        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/submissions")
            .Respond(HttpStatusCode.OK, "application/json",
                JsonSerializer.Serialize(submissions ?? [], _apiJsonOptions));

        if (dto.RequiresSignature)
        {
            _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/sign-off-statuses")
                .Respond(HttpStatusCode.OK, "application/json",
                    JsonSerializer.Serialize(signOff ?? [], _apiJsonOptions));
        }
    }

    private IRenderedComponent<DetailPage> RenderDetail(AssignmentSummaryDto dto) =>
        Render<DetailPage>(parameters => parameters.Add(page => page.Id, dto.Id));

    // ── AC4: one shell-owned heading, zero tabs, both regions ─────────────────────────

    [TestMethod]
    public void Detail_RendersOneShellHeading_AndNoTabs()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("h2.authoring-heading").Should().ContainSingle(
                "AC4/R2 — the shell owns the page's only action-bar heading");
            cut.FindAll("h1").Should().BeEmpty("this page renders no h1 at all — the shell's h2 is the heading");
            cut.FindAll("fluent-tab").Should().BeEmpty("the tabs are the regression this rewrite removes");
            cut.Find("#assignment-audience").Should().NotBeNull("the main region carries the audience block");
            cut.Find("#assignment-status").Should().NotBeNull("the main region carries the Status block");
            cut.Find("#assignment-rail").Should().NotBeNull("the rail region exists for its blocks");
        }, TimeSpan.FromSeconds(15));
    }

    // ── AC4b / R1 / P8: the disabled primary and its reason, never a 404 link ────────

    [TestMethod]
    public void Detail_Published_PrimaryIsOpenProgress_DisabledWithAVisibleReason_AndNoProgressHref()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            var primary = cut.Find("#assignment-primary-action");
            primary.TextContent.Trim().Should().Be("Open progress", "R1 pins the published primary");
            primary.HasAttribute("disabled").Should().BeTrue("the route does not exist until round b");

            cut.Find("#assignment-progress-reason").TextContent.Trim()
                .Should().NotBeEmpty("a disabled control states why it cannot be used");

            // AC4b — the guard scans ANY element carrying an href, not just <a>: FluentLink renders
            // <fluent-link href="…">, which an anchor-only selector never sees and which would let a
            // 404ing /progress link through this assertion unnoticed.
            var progressHrefs = cut.FindAll("[href]")
                .Select(host => host.GetAttribute("href"))
                .Where(href => href is not null && href.Contains("/progress", StringComparison.Ordinal))
                .ToList();
            progressHrefs.Should().BeEmpty("the page must never carry a link that 404s");

            cut.FindAll("fluent-button").Should().Contain(button => button.TextContent.Trim() == "Unpublish",
                "R1 keeps Unpublish live beside the disabled primary");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_StatusPanel_RendersDisabledProgress_WithItsReason()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#assignment-status-foot fluent-button")
                .Should().Contain(button => button.TextContent.Trim() == "Open progress");
            cut.Find("#assignment-status-progress-reason").TextContent.Trim()
                .Should().Be("Progress page arrives with the submission queue",
                    "P8 pins the reason the progress affordance cannot be used yet");
        }, TimeSpan.FromSeconds(15));
    }

    // ── The audience table's verdict set, in both states (§4.2, Q152) ─────────────────

    [TestMethod]
    public void Detail_Published_DeliveryPanel_RendersTheLogVerdicts()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        var sentAt = DateTimeOffset.Now.AddMinutes(-30);
        var openedAt = DateTimeOffset.Now.AddMinutes(-5);

        SetupGetAssignment(dto, Audience(
        [
            UnreachedStudent("Adeyemi, Tolu"),
            new AudienceStudentDto(Guid.NewGuid(), "Okafor, Ngozi", true,
            [
                Contact(AudienceDeliveryStateDto.Sent, sentAt: sentAt, openedAt: openedAt),
                Contact(AudienceDeliveryStateDto.Failed, ContactChannelDto.SMS, failureReason: "mailbox full",
                    retryScheduled: true)
            ]),
            new AudienceStudentDto(Guid.NewGuid(), "Bello, Ifeoma", true,
                [Contact(AudienceDeliveryStateDto.Skipped, failureReason: "guardian opted out")]),
            new AudienceStudentDto(Guid.NewGuid(), "Hassan, Yusuf", true,
                [Contact(AudienceDeliveryStateDto.Unknown)])
        ]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-audience-title").TextContent.Trim()
                .Should().Be("Delivery", "the published panel is titled Delivery");

            var table = cut.Find("#assignment-audience-table");
            var verdicts = table.QuerySelectorAll("tbody td.audience-table__verdict")
                .Select(cell => cell.TextContent.Trim()).ToList();

            verdicts.Should().Contain("Opened", "Sent plus the recipient's opened stamp reads Opened");
            verdicts.Should().Contain("Failed");
            verdicts.Should().Contain("Skipped");
            verdicts.Should().Contain("Unknown", "AC3 — a missing log row is never a fabricated Sent");
            verdicts.Should().Contain("Not reached", "AC2 — an unreached student is a visible row");

            table.TextContent.Should().Contain("mailbox full · retry scheduled");
            table.TextContent.Should().Contain("guardian opted out");
            table.TextContent.Should().Contain("2 · Primary",
                "guardian role and count only — no guardian name, no address (§4.9)");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_Draft_TargetsAndAudiencePanel_RendersTheAtPublishVerdicts()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft, title: "Yeast respiration write-up");
        SetupGetAssignment(dto, Audience(
        [
            UnreachedStudent("Adeyemi, Tolu"),
            new AudienceStudentDto(Guid.NewGuid(), "Okafor, Ngozi", true,
                [Contact(AudienceDeliveryStateDto.NotPublishingYet)]),
            new AudienceStudentDto(Guid.NewGuid(), "Bello, Ifeoma", false,
                [Contact(AudienceDeliveryStateDto.BlockedByPolicy)])
        ]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-audience-title").TextContent.Trim()
                .Should().Be("Targets & Audience", "a draft swaps Delivery for Targets & Audience");

            var table = cut.Find("#assignment-audience-table");
            var verdicts = table.QuerySelectorAll("tbody td.audience-table__verdict")
                .Select(cell => cell.TextContent.Trim()).ToList();

            verdicts.Should().Contain("Will notify");
            verdicts.Should().Contain("Blocked by policy", "the filter's verdict is shown, not silence");
            verdicts.Should().Contain("No contact");
        }, TimeSpan.FromSeconds(15));
    }

    // ── AC6 / P7: pager presence and the cap notice ──────────────────────────────────

    [TestMethod]
    public void Detail_AudienceThatFits_RendersNoPager_AndNoRowsPerPageControl()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience(Enumerable.Range(1, 8)
            .Select(index => new AudienceStudentDto(Guid.NewGuid(), $"Student {index:D2}", true,
                [Contact(AudienceDeliveryStateDto.Sent, sentAt: DateTimeOffset.Now)]))));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("tbody tr").Count.Should().Be(8);
            cut.FindComponents<FluentPaginator>().Should().BeEmpty(
                "Q162 — a set that fits on one page renders no pager");
            cut.FindAll("#assignment-audience-rows-per-page").Should().BeEmpty(
                "and no rows-per-page selector either");
            cut.Find("#assignment-audience-range").TextContent.Trim()
                .Should().StartWith("Showing 1–8 of 8");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_AudienceThatOverflows_PagesAtFifteen()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience(Enumerable.Range(1, 28)
            .Select(index => new AudienceStudentDto(Guid.NewGuid(), $"Student {index:D2}", true,
                [Contact(AudienceDeliveryStateDto.Sent, sentAt: DateTimeOffset.Now)]))));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-audience-range").TextContent.Trim()
                .Should().StartWith("Showing 1–15 of 28", "AC6 pins the first page of the 15-per-page grid");
            cut.FindComponents<FluentPaginator>().Should().NotBeEmpty("a 28-row set pages");
            cut.Find("#assignment-audience-rows-per-page").Should().NotBeNull();
            cut.FindAll("tbody tr").Count.Should().Be(15, "only the current page renders");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_ACappedAudience_StatesTheCapWhereThePagerIs()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience(
            Enumerable.Range(1, AudienceLimits.AudienceCap)
                .Select(index => new AudienceStudentDto(Guid.NewGuid(), $"Student {index:D3}", true,
                    [Contact(AudienceDeliveryStateDto.Sent, sentAt: DateTimeOffset.Now)])),
            truncated: true));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() => cut.Find("#assignment-audience-range").TextContent.Trim()
            .Should().StartWith("Showing 1–15 of 200 (capped)",
                "Q146 — the read is capped and the cap is stated where the pager is"),
            TimeSpan.FromSeconds(15));
    }

    // ── R7/D2 + P7: Details embeds the authoring body; Questions is the page's own table ──

    [TestMethod]
    public void Detail_DetailsPanel_EmbedsTheAuthoringBody_WithAnEditLink()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-details-title").TextContent.Trim().Should().Be("Details");

            var details = cut.Find("#assignment-details");
            details.QuerySelector("dl.authoring-summary__facts").Should().NotBeNull(
                "R7/D2 — Details is the authoring surface's own read-out, never a forked second layout (NFR-4)");

            var edit = details.QuerySelector("#assignment-details-edit")!;
            edit.TextContent.Trim().Should().Be("Edit ▸", "the one header action the Details panel carries");
            edit.GetAttribute("href").Should().Be($"/assignments/{dto.Id}/edit");

            details.QuerySelector("#authoring-breadcrumb").Should().BeNull("the embedded body brings no second trail");
            details.QuerySelector(".authoring-actionbar").Should().BeNull("and no second action bar");
            cut.FindAll("#authoring-breadcrumb").Should().ContainSingle("only the page's own trail");
            cut.FindAll("#authoring-questions").Should().BeEmpty("the page owns Questions");
            cut.FindAll("#authoring-targets").Should().BeEmpty("and Targets & Audience");
            cut.FindAll("h1").Should().BeEmpty("the embedded body adds no heading of its own");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_QuestionsTable_RendersTheFourFeasibleColumns_AndNoPointsColumn()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(
            dto,
            Audience([UnreachedStudent("Adeyemi, Tolu")]),
            questions:
            [
                Question(0, "Name the products", QuestionTypeDto.MultipleChoice, ("Glucose", false), ("Oxygen", true)),
                Question(1, "Why does the leaf float?", QuestionTypeDto.ShortAnswer)
            ]);

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            var headers = cut.FindAll("#assignment-questions-table thead th")
                .Select(cell => cell.TextContent.Trim()).ToList();
            string[] expectedHeaders = ["#", "Question", "Type", "Options"];
            headers.Should().Equal(expectedHeaders,
                "the feasible set — AssignmentQuestionReadDto has no points field, so no points column");

            var table = cut.Find("#assignment-questions-table");
            table.TextContent.Should().NotContain("pts");
            table.TextContent.Should().NotContain("Points");

            var rows = table.QuerySelectorAll("tbody tr").ToList();
            rows.Should().HaveCount(2);
            rows[0].TextContent.Should().Contain("Oxygen ✓", "the correct option is marked in the Options cell");
            rows[0].QuerySelector(".questions-table__number")!.TextContent.Trim().Should().Be("1");
            rows[1].QuerySelector(".questions-table__type")!.TextContent.Trim().Should().Be("Short answer");
            rows[1].QuerySelector(".questions-table__options")!.TextContent.Trim().Should()
                .Be("—", "a short-answer row carries no options — the payload has none");

            cut.FindAll("#assignment-questions fluent-button").Should().BeEmpty(
                "read-only: the view page exposes no editing affordance for a question");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_QuestionsThatFit_RenderNoPager()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(
            dto,
            Audience([UnreachedStudent("Adeyemi, Tolu")]),
            questions: Enumerable.Range(1, 8)
                .Select(index => Question(index - 1, $"Question {index:D2}"))
                .ToList());

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#assignment-questions-table tbody tr").Count.Should().Be(8);
            cut.FindComponents<FluentPaginator>().Should().BeEmpty(
                "Q162 — a set that fits on one page renders no pager");
            cut.FindAll("#assignment-questions-rows-per-page").Should().BeEmpty(
                "and no rows-per-page selector either");
            cut.Find("#assignment-questions-range").TextContent.Trim().Should().Be("Showing 1–8 of 8");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_QuestionsThatOverflow_PageAtFifteen()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(
            dto,
            Audience([UnreachedStudent("Adeyemi, Tolu")]),
            questions: Enumerable.Range(1, 28)
                .Select(index => Question(index - 1, $"Question {index:D2}"))
                .ToList());

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-questions-range").TextContent.Trim()
                .Should().Be("Showing 1–15 of 28", "AC6 pins the first page of the 15-per-page grid");
            cut.FindAll("#assignment-questions-table tbody tr").Count.Should().Be(15, "only the current page renders");
            cut.FindComponents<FluentPaginator>().Should().ContainSingle("a 28-row set pages");
            cut.Find("#assignment-questions-rows-per-page").Should().NotBeNull();
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_NoQuestions_RenderTheEmptyLine_NotAnEmptyBox()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-questions-empty").TextContent.Trim().Should().Be("No questions yet.");
            cut.FindAll("#assignment-questions-table").Should().BeEmpty("an empty set is a line, not an empty box");
            cut.FindAll("#assignment-questions-footer").Should().BeEmpty("no range and no pager for no rows");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_QuestionsReadFails_DegradesToAMessageInsideThePanel()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]), childrenReadFails: true);

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-questions-unavailable").TextContent.Should()
                .Contain("could not be loaded", "a supporting outage never fails the page");
            cut.Find("#assignment-audience").Should().NotBeNull("Delivery still renders");
            cut.Find("#assignment-status").Should().NotBeNull("and so does the Status panel");
        }, TimeSpan.FromSeconds(15));
    }

    // ── R3: the Status panel renders on a draft too, with one reason line ────────────
    [TestMethod]
    public void Detail_Draft_StatusPanel_RendersDraftWording()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft, title: "Yeast respiration write-up");
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-status-progress").TextContent.Should().Contain("Not published yet");
            cut.Find("#assignment-status-signoff").TextContent.Should().Contain("Not requested");
            cut.Find("#assignment-status-notifications").TextContent.Should().Contain("Nothing sent");
            cut.Find("#assignment-status-draft-reason").TextContent.Should()
                .Contain("Delivery, sign-off and progress all start at publish");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_Published_StatusPanel_ReportsTheCohortAndTheSendout()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(
            dto,
            Audience(
            [
                new AudienceStudentDto(Guid.NewGuid(), "Okafor, Ngozi", true,
                [
                    Contact(AudienceDeliveryStateDto.Sent, sentAt: DateTimeOffset.Now),
                    Contact(AudienceDeliveryStateDto.Failed, ContactChannelDto.SMS, failureReason: "mailbox full")
                ]),
                new AudienceStudentDto(Guid.NewGuid(), "Bello, Ifeoma", false,
                    [Contact(AudienceDeliveryStateDto.Skipped)])
            ]),
            submissions:
            [
                new SubmissionForReviewDto(Guid.NewGuid(), dto.Id, dto.Title, Guid.NewGuid(), 1,
                    ReviewStateDto.Graded, DateTimeOffset.UtcNow)
            ]);

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            var progress = cut.Find("#assignment-status-progress").TextContent;
            progress.Should().Contain("Cohort 2");
            progress.Should().Contain("Submitted 1 · Graded 1");
            progress.Should().Contain("Reached 1");

            cut.Find("#assignment-status-notifications").TextContent.Should().Contain("Sent 1");
            cut.Find("#assignment-status-notifications").TextContent.Should().Contain("Failed 1");
        }, TimeSpan.FromSeconds(15));
    }

    // ── The status pill vocabulary (§4.5) and the sign-off chip ──────────────────────

    [TestMethod]
    public void Detail_Published_StatusIsASemanticPill()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            var pill = cut.Find("#assignment-status-pill");
            pill.TextContent.Trim().Should().Be("Published");
            pill.ClassList.Should().Contain("status-pill--success", "published takes the success variant");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_RequiresSignature_RendersTheSignOffChipWithItsCounts()
    {
        var dto = MakeDto(AssignmentStatusDto.Published, requiresSignature: true);
        var studentId = Guid.NewGuid();
        SetupGetAssignment(
            dto,
            Audience([UnreachedStudent("Adeyemi, Tolu")]),
            signOff:
            [
                new SignOffStatusDto(studentId, "Okafor, Ngozi", Guid.NewGuid(), "Guardian", Guid.NewGuid(),
                    "Guardian", SignOffStateDto.Signed, DateTimeOffset.UtcNow, null, true, true, 1, null, null),
                new SignOffStatusDto(Guid.NewGuid(), "Bello, Ifeoma", Guid.NewGuid(), "Guardian", null,
                    null, SignOffStateDto.AwaitingSignature, null, null, true, false, null, null, null)
            ]);

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() => cut.Find("#assignment-signoff-chip").TextContent.Trim()
            .Should().Be("Sign-off 1/2"), TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_NotRequiresSignature_RendersNoSignOffChip()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() => cut.Find("#assignment-status").Should().NotBeNull());
        cut.FindAll("#assignment-signoff-chip").Should().BeEmpty();
    }

    // ── The approval chip (the decision buttons are the held Approval block) ─────────

    [TestMethod]
    public void Detail_RequiresApproval_RendersTheApprovalChip_ForEachDecisionState()
    {
        foreach (var (status, expected) in new[]
        {
            ((ApprovalStatusDto?)null, "Approval not submitted"),
            (ApprovalStatusDto.Pending, "Approval outstanding"),
            (ApprovalStatusDto.Approved, "Approval ✓ approved"),
            (ApprovalStatusDto.Rejected, "Approval rejected"),
            (ApprovalStatusDto.Denied, "Approval denied")
        })
        {
            var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: status, requiresApproval: true);
            SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

            var cut = RenderDetail(dto);

            cut.WaitForAssertion(() => cut.Find("#assignment-approval-chip").TextContent.Trim()
                .Should().Be(expected, $"the chip names the {status?.ToString() ?? "not-submitted"} decision"),
                TimeSpan.FromSeconds(15));
        }
    }

    [TestMethod]
    public void Detail_ApprovalNotRequired_RendersNoApprovalChip()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() => cut.Find("#assignment-status").Should().NotBeNull());
        cut.FindAll("#assignment-approval-chip").Should().BeEmpty();
    }

    // ── WS-A4 / spec §3.1 — duplicate-as-template, now a page-menu action ────────────

    private static void OpenPageActionsMenu(IRenderedComponent<DetailPage> cut)
    {
        cut.WaitForAssertion(
            () => cut.FindAll("fluent-button[title=\"Page actions\"]").Should().NotBeEmpty(
                "the page's kebab renders beside the title"),
            TimeSpan.FromSeconds(15));
        cut.Find("fluent-button[title=\"Page actions\"]").Click();
    }

    /// <summary>Mocks the confirm dialog (<c>ShowConfirmDialogAsync</c> resolves to
    /// <c>ShowDialogAsync&lt;ConfirmDialog, ConfirmDialogContent&gt;</c>) at the requested outcome.</summary>
    private Mock<DialogService> SetupConfirmDialogResult(bool confirmed)
    {
        var result = confirmed ? DialogResult.Ok<object?>(null) : DialogResult.Cancel();
        var dialogMock = this.RegisterDialogService();
        dialogMock
            .Setup(dialog => dialog.ShowDialogAsync(typeof(ConfirmDialog), It.IsAny<DialogOptions>()))
            .ReturnsAsync(result);
        return dialogMock;
    }

    [TestMethod]
    public void Detail_PageMenu_OffersDuplicate()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        OpenPageActionsMenu(cut);
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item").Select(item => item.TextContent.Trim())
            .Should().Contain("Duplicate"));
    }

    [TestMethod]
    public void Detail_DuplicateConfirmed_PostsAndNavigatesToTheCopy()
    {
        var sourceId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var dto = MakeDto(AssignmentStatusDto.Published) with { Id = sourceId };
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));
        SetupConfirmDialogResult(confirmed: true);

        var cut = RenderDetail(dto);

        OpenPageActionsMenu(cut);
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item").Select(item => item.TextContent.Trim())
            .Should().Contain("Duplicate"));

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{sourceId}/duplicate")
            .Respond(HttpStatusCode.Created, "application/json", JsonSerializer.Serialize(new { id = newId }));

        cut.FindAll("fluent-menu-item").Single(item => item.TextContent.Trim() == "Duplicate").MenuItemClick();

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            Services.GetRequiredService<NavigationManager>().Uri
                .Should().EndWith($"/assignments/{newId}", "confirming duplicate navigates to the copy");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_DuplicateDeclined_PostsNothing()
    {
        var sourceId = Guid.NewGuid();
        var dto = MakeDto(AssignmentStatusDto.Published) with { Id = sourceId };
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));
        var dialogMock = SetupConfirmDialogResult(confirmed: false);

        var postCount = 0;
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{sourceId}/duplicate")
            .Respond(_ =>
            {
                postCount++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

        var cut = RenderDetail(dto);

        OpenPageActionsMenu(cut);
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item").Select(item => item.TextContent.Trim())
            .Should().Contain("Duplicate"));

        cut.FindAll("fluent-menu-item").Single(item => item.TextContent.Trim() == "Duplicate").MenuItemClick();

        cut.WaitForAssertion(() => dialogMock.Verify(
            dialog => dialog.ShowDialogAsync(typeof(ConfirmDialog), It.IsAny<DialogOptions>()),
            Times.Once));

        postCount.Should().Be(0, "declining the confirm dialog must not fire the duplicate POST");
        Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/",
            "declining must not navigate away from the source assignment");
    }

    [TestMethod]
    public void Detail_DuplicateFails_RendersTheFriendlyErrorLine_AndStays()
    {
        var sourceId = Guid.NewGuid();
        var dto = MakeDto(AssignmentStatusDto.Published) with { Id = sourceId };
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));
        SetupConfirmDialogResult(confirmed: true);

        var cut = RenderDetail(dto);

        OpenPageActionsMenu(cut);
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item").Select(item => item.TextContent.Trim())
            .Should().Contain("Duplicate"));

        var duplicatePosts = 0;
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{sourceId}/duplicate")
            .Respond(_ =>
            {
                duplicatePosts++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });

        cut.FindAll("fluent-menu-item").Single(item => item.TextContent.Trim() == "Duplicate").MenuItemClick();

        cut.WaitForAssertion(() =>
        {
            duplicatePosts.Should().Be(1, "the confirmed duplicate fires the POST");
            cut.Find("#assignment-action-error").TextContent.Trim()
                .Should().Be("The assignment could not be duplicated. Try again.",
                    "the failure is this page's own sentence — a raw transport message never reaches the reader");
        }, TimeSpan.FromSeconds(15));

        Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/",
            "a failure must not navigate away from the source assignment");
    }

    // ── P5b (extended) / §2 / §11: the status-driven header and the lifecycle verbs ─────────────

    [TestMethod]
    public void Detail_PrimaryAction_IsStatusDriven_ForEveryStatus()
    {
        var expected = new (AssignmentStatusDto Status, string? Label)[]
        {
            (AssignmentStatusDto.Draft, "Publish"),
            (AssignmentStatusDto.Scheduled, "Unpublish"),
            (AssignmentStatusDto.Closed, "Archive"),
            (AssignmentStatusDto.Published, "Open progress"),
            (AssignmentStatusDto.Archived, null)
        };

        foreach (var (status, label) in expected)
        {
            var dto = MakeDto(status);
            SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

            var cut = RenderDetail(dto);

            cut.WaitForAssertion(() =>
            {
                if (label is null)
                {
                    cut.FindAll("#assignment-primary-action").Should().BeEmpty(
                        $"{status} is terminal — it offers no primary action at all");
                }
                else
                {
                    cut.Find("#assignment-primary-action").TextContent.Trim().Should().Be(label,
                        $"§11 pins the {status} primary");
                    if (status != AssignmentStatusDto.Published)
                    {
                        cut.Find("#assignment-primary-action").HasAttribute("disabled").Should().BeFalse(
                            $"{status}'s primary is live");
                    }
                }

                if (status != AssignmentStatusDto.Published)
                {
                    cut.FindAll("#assignment-progress-reason").Should().BeEmpty(
                        "only the published page carries the round-b route's reason");
                }
            }, TimeSpan.FromSeconds(15));
        }
    }

    [TestMethod]
    public void Detail_DraftWithOutstandingApproval_PrimaryIsDisabled_WithItsReason()
    {
        foreach (var approvalStatus in new[]
                 {
                     (ApprovalStatusDto?)null, ApprovalStatusDto.Pending, ApprovalStatusDto.Rejected
                 })
        {
            var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: approvalStatus, requiresApproval: true);
            SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

            var cut = RenderDetail(dto);

            cut.WaitForAssertion(() =>
            {
                var primary = cut.Find("#assignment-primary-action");
                primary.TextContent.Trim().Should().Be("Publish");
                primary.HasAttribute("disabled").Should().BeTrue(
                    $"approval is {approvalStatus?.ToString() ?? "not submitted"} — publishing must wait");
                cut.Find("#assignment-publish-reason").TextContent.Trim().Should()
                    .Be("Approval is required before this assignment can be published",
                        "AC4b — a disabled primary states why, and the approval is the reason");
            }, TimeSpan.FromSeconds(15));
        }
    }

    [TestMethod]
    public void Detail_ApprovedDraft_PrimaryIsLive()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Approved, requiresApproval: true);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-primary-action").HasAttribute("disabled").Should().BeFalse(
                "an approved draft publishes");
            cut.FindAll("#assignment-publish-reason").Should().BeEmpty("and there is nothing to explain");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_Draft_PageMenu_OffersSchedule()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        OpenPageActionsMenu(cut);
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item").Select(item => item.TextContent.Trim())
            .Should().Contain("Schedule…", "US7 — scheduling is not split across surfaces"));
    }

    [TestMethod]
    public void Detail_Scheduled_PageMenu_OffersReschedule()
    {
        var dto = MakeDto(AssignmentStatusDto.Scheduled);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        OpenPageActionsMenu(cut);
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item").Select(item => item.TextContent.Trim())
            .Should().Contain("Reschedule…"));
    }

    [TestMethod]
    public void Detail_Published_PageMenu_OffersCloseAndDuplicate()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        OpenPageActionsMenu(cut);
        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("fluent-menu-item").Select(item => item.TextContent.Trim()).ToList();
            items.Should().Contain("Close", "R1 splits the pair: Close is the kebab item");
            items.Should().Contain("Duplicate");
        });
    }

    // ── The Approval rail block (LIF-1, spec §3.1/§3.6; R4 body buttons) ───────────────────────

    /// <summary>Mocks the reason dialog shell (<c>ShowShellDialogAsync&lt;ApprovalDecisionReasonDialog, …&gt;</c>
    /// resolves to <c>ShowDialogAsync(typeof(ApprovalDecisionReasonDialog), …)</c> and unwraps the
    /// <see cref="DialogShellResult{TResult}"/> the extension hands back).</summary>
    private void SetupReasonDialogResult(string? reason)
    {
        var payload = new DialogShellResult<ApprovalDecisionReasonDialog.ReasonResult>(
            new ApprovalDecisionReasonDialog.ReasonResult(reason));
        var dialogMock = this.RegisterDialogService();
        dialogMock
            .Setup(dialog => dialog.ShowDialogAsync(typeof(ApprovalDecisionReasonDialog), It.IsAny<DialogOptions>()))
            .ReturnsAsync(DialogResult.Ok<object?>(payload));
    }

    private static void ClickApprovalButton(IRenderedComponent<DetailPage> cut, string label) =>
        cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == label).Click();

    [TestMethod]
    public void Detail_ApprovalSubmit_PostsAndRefreshes()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft, requiresApproval: true);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);
        cut.WaitForAssertion(() => cut.FindAll("fluent-button")
            .Should().Contain(button => button.TextContent.Trim() == "Submit for approval"),
            TimeSpan.FromSeconds(15));

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/submit-for-approval")
            .Respond(HttpStatusCode.NoContent);

        ClickApprovalButton(cut, "Submit for approval");

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            _assignmentGetCount.Should().BeGreaterThanOrEqualTo(2,
                "the page re-reads the assignment after the submission succeeds");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_ApprovalApprove_PostsTheActingTeacher_AndRefreshes()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Pending, requiresApproval: true);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));
        SetupConfirmDialogResult(confirmed: true);

        var cut = RenderDetail(dto);
        cut.WaitForAssertion(() => cut.FindAll("fluent-button")
            .Should().Contain(button => button.TextContent.Trim() == "Approve"), TimeSpan.FromSeconds(15));

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/approve")
            .WithContent(JsonSerializer.Serialize(new ApproveAssignmentRequest(Guid.Empty), _apiJsonOptions))
            .Respond(HttpStatusCode.NoContent);

        ClickApprovalButton(cut, "Approve");

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            _assignmentGetCount.Should().BeGreaterThanOrEqualTo(2,
                "the page re-reads the assignment after the approval succeeds");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_ApprovalApprove_Declined_PostsNothing()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Pending, requiresApproval: true);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));
        var dialogMock = SetupConfirmDialogResult(confirmed: false);

        var approvePosts = 0;
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/approve")
            .Respond(_ =>
            {
                approvePosts++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

        var cut = RenderDetail(dto);
        cut.WaitForAssertion(() => cut.FindAll("fluent-button")
            .Should().Contain(button => button.TextContent.Trim() == "Approve"), TimeSpan.FromSeconds(15));

        var loadsBeforeDecline = _assignmentGetCount;

        ClickApprovalButton(cut, "Approve");

        cut.WaitForAssertion(() => dialogMock.Verify(
            dialog => dialog.ShowDialogAsync(typeof(ConfirmDialog), It.IsAny<DialogOptions>()),
            Times.Once));

        approvePosts.Should().Be(0, "declining the confirm dialog must not record a decision");
        _assignmentGetCount.Should().Be(loadsBeforeDecline, "and must not reload the page either");
    }

    [TestMethod]
    public void Detail_ApprovalReject_PostsTheReason()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Pending, requiresApproval: true);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));
        SetupReasonDialogResult("Add the rubric");

        var cut = RenderDetail(dto);
        cut.WaitForAssertion(() => cut.FindAll("fluent-button")
            .Should().Contain(button => button.TextContent.Trim() == "Reject…"), TimeSpan.FromSeconds(15));

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/reject")
            .WithContent(JsonSerializer.Serialize(
                new RejectAssignmentRequest(Guid.Empty, "Add the rubric"), _apiJsonOptions))
            .Respond(HttpStatusCode.NoContent);

        ClickApprovalButton(cut, "Reject…");

        cut.WaitForAssertion(() => _mockHttp.VerifyNoOutstandingExpectation(), TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_ApprovalDeny_PostsTheReason()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Pending, requiresApproval: true);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));
        SetupReasonDialogResult("Out of scope");

        var cut = RenderDetail(dto);
        cut.WaitForAssertion(() => cut.FindAll("fluent-button")
            .Should().Contain(button => button.TextContent.Trim() == "Deny…"), TimeSpan.FromSeconds(15));

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/deny")
            .WithContent(JsonSerializer.Serialize(
                new DenyAssignmentRequest(Guid.Empty, "Out of scope"), _apiJsonOptions))
            .Respond(HttpStatusCode.NoContent);

        ClickApprovalButton(cut, "Deny…");

        cut.WaitForAssertion(() => _mockHttp.VerifyNoOutstandingExpectation(), TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_ApprovalReasonDialogCancelled_PostsNothing()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Pending, requiresApproval: true);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));
        var dialogMock = this.RegisterDialogService();
        dialogMock
            .Setup(dialog => dialog.ShowDialogAsync(
                typeof(ApprovalDecisionReasonDialog), It.IsAny<DialogOptions>()))
            .ReturnsAsync(DialogResult.Cancel());

        var rejectPosts = 0;
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/reject")
            .Respond(_ =>
            {
                rejectPosts++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

        var cut = RenderDetail(dto);
        cut.WaitForAssertion(() => cut.FindAll("fluent-button")
            .Should().Contain(button => button.TextContent.Trim() == "Reject…"), TimeSpan.FromSeconds(15));

        ClickApprovalButton(cut, "Reject…");

        cut.WaitForAssertion(() => dialogMock.Verify(
            dialog => dialog.ShowDialogAsync(
                typeof(ApprovalDecisionReasonDialog), It.IsAny<DialogOptions>()),
            Times.Once));

        rejectPosts.Should().Be(0, "cancelling the reason dialog must not record a decision");
    }

    [TestMethod]
    public void Detail_ApprovalPanel_Approved_IsReadOnlyWithNoButtons()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft, approvalStatus: ApprovalStatusDto.Approved, requiresApproval: true);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-approval-approved").TextContent.Trim().Should().Be("Approval ✓ approved");
            cut.FindAll("#assignment-approval fluent-button").Should().BeEmpty(
                "an approved draft has no decision left to take");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_ApprovalPanel_IsAbsentWhenApprovalIsNotRequired()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() => cut.Find("#assignment-status").Should().NotBeNull());
        cut.FindAll("#assignment-approval").Should().BeEmpty(
            "the panel is gated on the server-derived RequiresApproval");
    }

    [TestMethod]
    public void Detail_ApprovalPanel_IsAbsentOnAPublishedAssignment()
    {
        var dto = MakeDto(AssignmentStatusDto.Published, approvalStatus: ApprovalStatusDto.Approved,
            requiresApproval: true);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() => cut.Find("#assignment-status").Should().NotBeNull());
        cut.FindAll("#assignment-approval").Should().BeEmpty(
            "Approval joins the rail before publish and leaves it once the assignment is out");
        cut.Find("#assignment-approval-chip").Should().NotBeNull("the header chip still states the decision");
    }

    // ── The rail: Content & Resources and Rules ─────────────

    [TestMethod]
    public void Detail_MaterialsPanel_ListsAttachmentsWithTheirGroundingState_AndLinksResources()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(
            dto,
            Audience([UnreachedStudent("Adeyemi, Tolu")]),
            attachments:
            [
                Attachment("Lab safety sheet.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
                Attachment("Bench setup.png", "image/png", AttachmentExtractionStatusDto.Unsupported),
                Attachment("Practical demo.mp4", "video/mp4", AttachmentExtractionStatusDto.Failed,
                    extractionError: "codec unsupported")
            ],
            resources: [Resource("Royal Society — photosynthesis", "https://royalsociety.org/photosynthesis")]);

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-materials-title").TextContent.Trim().Should().Be("Content & Resources");
            cut.Find("#assignment-materials").TextContent.Should().Contain("4",
                "the count is the panel's whole row set: three attachments and one reference");

            var rows = cut.FindAll("#assignment-materials-list .materials-row");
            rows.Should().HaveCount(4);

            var states = cut.FindAll("#assignment-materials-list .materials-row__meta")
                .Select(cell => cell.TextContent.Trim()).ToList();
            states.Should().BeEquivalentTo(
            [
                "grounds ✓ · extracted text",
                "image only · not grounded",
                "extraction failed — codec unsupported"
            ], "the grounding readout is derived from the persisted extraction outcome, per row");

            cut.Find("#assignment-materials-list fluent-link").GetAttribute("href")
                .Should().Be("https://royalsociety.org/photosynthesis", "a reference renders as a link");
            cut.Find("#assignment-materials-manage").GetAttribute("href")
                .Should().Be($"/assignments/{dto.Id}/edit", "the panel's one header action");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_MaterialsPanel_WithNoMaterials_RendersTheEmptyLine()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-materials-empty").TextContent.Trim().Should().Be("No materials yet.");
            cut.FindAll("#assignment-materials-list").Should().BeEmpty();
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_RulesPanel_RendersEveryResolvedPolicyField_WithItsOverrideMark()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        SetupGetAssignment(
            dto,
            Audience([UnreachedStudent("Adeyemi, Tolu")]),
            policy: new EffectiveAssignmentPolicy(
                SignatureRequirement: SignatureRequirementMode.Mandatory,
                RequiresApprovalBeforePublish: true,
                MaxPrimaryContacts: 2,
                MaxCopyContacts: null,
                MandatoryReview: true,
                ArchiveGraceDays: 14,
                ApprovalWindowDays: 3,
                SignatureRequirementFromOverride: true,
                RequiresApprovalBeforePublishFromOverride: false,
                MaxPrimaryContactsFromOverride: false,
                MaxCopyContactsFromOverride: false,
                MandatoryReviewFromOverride: false,
                ArchiveGraceDaysFromOverride: true,
                ApprovalWindowDaysFromOverride: false));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-rules-title").TextContent.Trim().Should().Be("Rules");

            var labels = cut.FindAll("#assignment-rules-list .rules-row__label")
                .Select(cell => cell.TextContent.Trim()).ToList();
            string[] expectedLabels =
            [
                "Signature requirement",
                "Approval before publish",
                "Max primary contacts",
                "Max copy contacts",
                "Mandatory review",
                "Archive grace days",
                "Approval window"
            ];
            labels.Should().Equal(expectedLabels,
                "one row per real EffectiveAssignmentPolicy field, and nothing invented");

            var values = cut.FindAll("#assignment-rules-list .rules-row__value")
                .Select(cell => FlatText(cell)).ToList();
            values[0].Should().Be("Required after completion (override)");
            values[1].Should().Be("Required");
            values[2].Should().Be("2");
            values[3].Should().Be("Uncapped");
            values[4].Should().Be("Required");
            values[5].Should().Be("14 days (override)");
            values[6].Should().Be("3 days");

            cut.Find("#assignment-rules").TextContent.Should().NotContain("5 slots",
                "the mock's slots/chars line has no backing field and is dropped, not invented");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_RulesPanel_DegradesToItsOwnLine_WhenThePolicyReadFails()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]), policyReadFails: true);

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-rules-unavailable").TextContent.Trim().Should().Be("Policy unavailable.");
            cut.FindAll("#assignment-rules-list").Should().BeEmpty();
            cut.Find("#assignment-audience").Should().NotBeNull("a supporting outage never fails the page");
            cut.Find("#assignment-status").Should().NotBeNull();
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_RailPanels_CarryTheirOwnIds()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft, requiresApproval: true);
        SetupGetAssignment(dto, Audience([UnreachedStudent("Adeyemi, Tolu")]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            var rail = cut.Find("#assignment-rail");
            rail.QuerySelector("#assignment-materials-title")!.TextContent.Trim().Should().Be("Content & Resources");
            rail.QuerySelector("#assignment-rules-title")!.TextContent.Trim().Should().Be("Rules");
            rail.QuerySelector("#assignment-approval-title")!.TextContent.Trim().Should().Be("Approval");
        }, TimeSpan.FromSeconds(15));
    }

    /// <summary>An element's text with Razor's line breaks and indentation collapsed, so an assertion
    /// reads the text rather than the markup's whitespace.</summary>
    private static string FlatText(AngleSharp.Dom.IElement element) =>
        string.Join(' ', element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The circuit principal the approval verbs read the acting teacher from. It carries no
    /// <c>teacher_id</c> claim (the dev/TestAuth default), so an approval body carries the server's
    /// <c>Guid.Empty</c> placeholder.</summary>
    private sealed class StubAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity("test"))));
    }

    // ── Degraded reads are stated, never silently swallowed (plan P2) ────────────────

    [TestMethod]
    public void Detail_EmptyAudienceThatDegraded_SaysItCouldNotBeResolved_NotThatThereAreNoStudents()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, new AssignmentAudienceDto([], Truncated: false, Degraded: true));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-audience-unresolved").TextContent.Trim()
                .Should().Be("The audience could not be resolved. Reload the page to try again.",
                    "an empty-but-degraded audience is a failure, never 'no students'");
            cut.FindAll("#assignment-audience-empty").Should().BeEmpty(
                "the empty sentence would claim a resolve that did not happen");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_EmptyAudienceThatResolved_SaysThereAreNoStudents()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, Audience([]));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-audience-empty").TextContent.Trim()
                .Should().Be("No students resolve for this assignment's targets.");
            cut.FindAll("#assignment-audience-unresolved").Should().BeEmpty(
                "a healthy empty read is not an outage");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_AudienceReadThatFails_SaysSoInsteadOfSpinning()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, audienceReadFails: true);

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#assignment-audience-unavailable").TextContent.Trim()
                .Should().Be("The audience could not be loaded.",
                    "a supporting outage never fails the page, and never leaves a spinner running");
            cut.Find("#assignment-audience").QuerySelectorAll("fluent-spinner").Should().BeEmpty(
                "the panel is no longer loading — it failed");
        }, TimeSpan.FromSeconds(15));

        cut.Find("#assignment-status").Should().NotBeNull("the rest of the page is unaffected");
    }

    [TestMethod]
    public void Detail_DegradedAudience_StatesTheTwoOutagesSeparately()
    {
        var dto = MakeDto(AssignmentStatusDto.Published);
        SetupGetAssignment(dto, new AssignmentAudienceDto(
            [UnreachedStudent(string.Empty),
             new AudienceStudentDto(Guid.NewGuid(), "Hassan, Yusuf", true,
                 [Contact(AudienceDeliveryStateDto.Unknown)])],
            Truncated: false,
            Degraded: true));

        var cut = RenderDetail(dto);

        cut.WaitForAssertion(() =>
        {
            var note = cut.Find("#assignment-audience-degraded").TextContent;
            note.Should().Contain("blank student name", "a directory failure is reported as itself");
            note.Should().Contain("delivery log", "and a log failure as itself");
            cut.Find("#assignment-audience-table").TextContent.Should().Contain("Unknown");
        }, TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void Detail_UnknownAssignment_ShowsNotFound()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/*")
            .Respond(HttpStatusCode.NotFound);

        var cut = Render<DetailPage>(parameters => parameters.Add(page => page.Id, Guid.NewGuid()));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Assignment not found"));
    }
}
