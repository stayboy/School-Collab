using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-E2 / ar-17 — the <c>NotificationFailuresSection</c> card on the Assignments
/// Detail page. Covers the round-doc binding list:
/// <list type="bullet">
///   <item><c>Rows_Render_WithFutureRetryBadge</c> — row rendering (channel/kind/attempt/reason + retry badge).</item>
///   <item><c>Empty_ShowsInfoBar_NoTable</c> — the Info empty state.</item>
///   <item><c>Terminal_Vs_Retrying_Badges</c> — <c>NextRetryAt == null</c> → Neutral "Failed — no more retries"; future → Lightweight "Retry scheduled …".</item>
///   <item><c>Error_ShowsErrorBar_NoTable_NotEmpty</c> — a throwing backend renders the Error bar, never the empty state.</item>
///   <item><c>ClientPath_HitsFailuresEndpoint_DeserializesStringKinds</c> — the request hits GET /assignments/{id}/notification-failures and <c>NotificationKindDto</c> round-trips from its string form (pins read-side tolerance; see the test comment — the live API emits these delivery enums numerically).</item>
///   <item><c>NoLeakage_ShortIdFallback_ContactLabel</c> — no path-shaped strings / full guids; matched contacts render their value, unmatched fall back to a short id.</item>
/// </list>
/// Same Blazor + MockHttp + bUnit pattern as <c>SignOffSectionBunitTests</c>; the
/// section self-loads over the tenant-scoped failures endpoint and enriches recipient
/// labels from the guardian contact list (<c>StudentsApiClient</c>) — decision (b).
/// </summary>
[TestClass]
public class NotificationFailuresSectionBunitTests : BunitContext
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UnmatchedContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;
    private int _failuresGetCount;

    public NotificationFailuresSectionBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        _apiJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter<ContactChannelDto>(),
                new JsonStringEnumConverter<NotificationKindDto>(),
            }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<NotificationFailuresSection>>());
        // Decision (b): the section enriches recipient labels from the guardian
        // contact list via StudentsApiClient (the Publish-dialog universe).
        Services.AddSingleton<SchoolCollab.Students.Application.Services.StudentsApiClient>();
        Services.AddSingleton<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<SchoolCollab.Students.Application.Services.StudentsApiClient>>());
    }

    private static NotificationFailureDto Row(
        ContactChannelDto channel,
        NotificationKindDto kind,
        int attempt = 3,
        string? reason = "Provider replied: 550 permanent failure",
        DateTimeOffset? nextRetry = null,
        Guid? contactId = null,
        Guid? recipientId = null) =>
        new(
            RecipientId: recipientId ?? Guid.NewGuid(),
            ContactId: contactId ?? ContactId,
            Channel: channel,
            Kind: kind,
            Attempt: attempt,
            FailureReason: reason,
            NextRetryAt: nextRetry);

    private void SetupFailures(params NotificationFailureDto[] rows)
    {
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{AssignmentId}/notification-failures")
            .Respond(_ =>
            {
                _failuresGetCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(rows, _apiJsonOptions),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });
    }

    private void SetupContacts(params SubscribedContactDto[] contacts)
    {
        // StudentsApiClient reads with the BCL Web-default options (enum-as-number),
        // not the Assignments string-converter set — plain Web serialization matches.
        _mockHttp.When(HttpMethod.Get, "http://localhost/contacts/subscribed?ownerType=Guardian&scope=AllAssignments")
            .Respond(HttpStatusCode.OK, "application/json",
                JsonSerializer.Serialize(contacts, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [TestMethod]
    public void Rows_Render_WithFutureRetryBadge()
    {
        var future = DateTimeOffset.UtcNow.AddHours(2);
        SetupFailures(
            Row(ContactChannelDto.Email, NotificationKindDto.Publish, attempt: 2, reason: "SMTP transient", nextRetry: future),
            Row(ContactChannelDto.SMS, NotificationKindDto.Reminder, attempt: 5, reason: "Carrier rejected"));
        SetupContacts();

        var cut = Render<NotificationFailuresSection>(p =>
            p.Add(x => x.AssignmentId, AssignmentId).Add(x => x.CanHaveFailures, true));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("table tbody tr").Should().HaveCount(2);
            cut.Markup.Should().Contain(EnumHelper.GetDescription(ContactChannelDto.Email));
            cut.Markup.Should().Contain(EnumHelper.GetDescription(NotificationKindDto.Publish));
            cut.Markup.Should().Contain(EnumHelper.GetDescription(NotificationKindDto.Reminder));
            cut.Markup.Should().Contain("SMTP transient");
            cut.Markup.Should().Contain("Carrier rejected");
            cut.Markup.Should().Contain("Retry scheduled");
        });
    }

    [TestMethod]
    public void Empty_ShowsInfoBar_NoTable()
    {
        SetupFailures();
        SetupContacts();

        var cut = Render<NotificationFailuresSection>(p =>
            p.Add(x => x.AssignmentId, AssignmentId).Add(x => x.CanHaveFailures, true));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("No failed notifications.");
            cut.Markup.Should().Contain("fluent-messagebar");
            cut.Markup.Should().Contain("intent-info",
                "the empty state is Information, not Success (zero rows is legitimate — null sender / unconfigured SMTP)");
            cut.Markup.Should().NotContain("notification-failures-table",
                "an empty result must never render the table");
        });
    }

    [TestMethod]
    public void Terminal_Vs_Retrying_Badges()
    {
        var future = DateTimeOffset.UtcNow.AddHours(1);
        // Decision (c): terminal row (NextRetryAt null) + retrying row (future time).
        var terminalId = Guid.NewGuid();
        var retryingId = Guid.NewGuid();
        SetupFailures(
            Row(ContactChannelDto.Email, NotificationKindDto.Publish, attempt: 5, nextRetry: null, contactId: terminalId),
            Row(ContactChannelDto.SMS, NotificationKindDto.Overdue, attempt: 2, nextRetry: future, contactId: retryingId));
        SetupContacts();

        var cut = Render<NotificationFailuresSection>(p =>
            p.Add(x => x.AssignmentId, AssignmentId).Add(x => x.CanHaveFailures, true));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Failed \u2014 no more retries",
                "a null NextRetryAt renders the terminal 'no more retries' badge");
            cut.Markup.Should().Contain("appearance=\"neutral\"",
                "the terminal badge uses the render-safe Neutral appearance (decision (c))");
            cut.Markup.Should().Contain("Retry scheduled",
                "a future NextRetryAt renders the retry-scheduled badge");
            cut.Markup.Should().Contain("appearance=\"lightweight\"",
                "the retrying badge uses the render-safe Lightweight appearance (decision (c))");
        });
    }

    [TestMethod]
    public void Error_ShowsErrorBar_NoTable_NotEmpty()
    {
        // Decision (e): the handler throws → the Error bar renders, never the empty
        // state. Scripted 500 (EnsureSuccessStatusCode surfaces HttpRequestException).
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{AssignmentId}/notification-failures")
            .Respond(HttpStatusCode.InternalServerError);
        SetupContacts();

        var cut = Render<NotificationFailuresSection>(p =>
            p.Add(x => x.AssignmentId, AssignmentId).Add(x => x.CanHaveFailures, true));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("fluent-messagebar");
            cut.Markup.Should().Contain("intent-error",
                "the load failure must render the Error message bar, not the empty state");
            cut.Markup.Should().MatchRegex("500|Internal Server Error",
                "the error bar surfaces the exception message");
            cut.Markup.Should().NotContain("No failed notifications.",
                "an error must never silently render as the empty state");
            cut.Markup.Should().NotContain("notification-failures-table",
                "an error must never render the table");
        });
    }

    [TestMethod]
    public void TransportCancellation_ShowsErrorBar_NotEmptyState()
    {
        // Review finding F2: a transport timeout/abort surfaces as an
        // OperationCanceledException (TaskCanceledException) with the CALLER's token
        // uncancelled. The original unfiltered `catch (OperationCanceledException) { }`
        // swallowed it and fell through to the Info "No failed notifications." bar —
        // precisely the state decision (e) forbids. The catch is now filtered on
        // ct.IsCancellationRequested, so only a dispose-cancel is swallowed.
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{AssignmentId}/notification-failures")
            .Throw(new TaskCanceledException("The request timed out."));
        SetupContacts();

        var cut = Render<NotificationFailuresSection>(p =>
            p.Add(x => x.AssignmentId, AssignmentId).Add(x => x.CanHaveFailures, true));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("intent-error",
                "a transport cancellation (token NOT cancelled) must surface as the error bar");
            cut.Markup.Should().NotContain("No failed notifications.",
                "it must never masquerade as the empty state (decision (e) / finding F2)");
        });
    }

    [TestMethod]
    public void ClientPath_HitsFailuresEndpoint_DeserializesStringKinds()
    {
        // Item 6: the client must hit GET /assignments/{id}/notification-failures and
        // deserialize NotificationKindDto from its STRING form. The payload is scripted
        // with string converters. Review finding F3: the live API registers ten sibling
        // enums as strings but NOT NotificationKindDto / ContactChannelDto, so the real
        // wire form for these two is numeric — this test therefore pins read-side
        // tolerance (and would fail if the tolerance were removed), not the live shape.
        // The API-side registration gap is recorded as a residual.
        SetupFailures(Row(ContactChannelDto.Email, NotificationKindDto.Publish, reason: "bounce"));
        SetupContacts();

        var cut = Render<NotificationFailuresSection>(p =>
            p.Add(x => x.AssignmentId, AssignmentId).Add(x => x.CanHaveFailures, true));

        cut.WaitForAssertion(() =>
        {
            _failuresGetCount.Should().BeGreaterThanOrEqualTo(1,
                "the section must request GET /assignments/{id}/notification-failures");
            cut.Markup.Should().Contain(EnumHelper.GetDescription(NotificationKindDto.Publish),
                "the Kind description renders — the enum deserialized from its string form");
            cut.Markup.Should().Contain(EnumHelper.GetDescription(ContactChannelDto.Email),
                "the Channel description renders");
        });
    }

    [TestMethod]
    public void NoLeakage_ShortIdFallback_ContactLabel()
    {
        // Item 7: matched contacts render their value label; unmatched fall back to a
        // short id; no full path/URL-shaped or non-truncated id leaks into markup.
        SetupFailures(
            Row(ContactChannelDto.Email, NotificationKindDto.Publish, contactId: ContactId),
            Row(ContactChannelDto.WhatsApp, NotificationKindDto.Completion, contactId: UnmatchedContactId));
        SetupContacts(new SubscribedContactDto(ContactId, ContactChannel.Email, "guardian@example.com", Role: null));

        var cut = Render<NotificationFailuresSection>(p =>
            p.Add(x => x.AssignmentId, AssignmentId).Add(x => x.CanHaveFailures, true));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("guardian@example.com",
                "the enriched recipient label renders for a matched contact (decision (b))");
            cut.Markup.Should().NotContain(UnmatchedContactId.ToString(),
                "an unmatched contact must not render its full GUID");
            cut.Markup.Should().NotContain(@"C:\", "no Windows path leakage");
            cut.Markup.Should().NotContain("/var/", "no Unix path leakage");
            cut.Markup.Should().NotContain("/home/", "no home-dir path leakage");
            cut.Markup.Should().NotContain("StoragePath", "no storage-path-shaped string");
            cut.Markup.Should().NotMatch("http://|https://", "no URL-shaped string leaks");
        });
    }

    [TestMethod]
    public void SameContact_MultipleKinds_RendersDistinctRows_AndSurvivesReRender()
    {
        // Regression (parent pre-review fix + review finding F1). Two distinct row
        // shapes must both render and survive a re-render over the materialised keyed
        // table, because a derived row key is NOT guaranteed unique:
        //   (1) one contact with several kinds — the (RecipientId, Kind) cardinality;
        //   (2) the F1 republish case — PublishAssignmentCommandHandler calls Publish()
        //       (which early-returns when already Published) and then STILL re-broadcasts
        //       over the reused recipients, so two rows can share RecipientId + ContactId
        //       + Kind + Attempt exactly. Row 3/4 below are byte-identical for that reason.
        // Duplicate keys are not rejected on first materialisation
        // (Rows_Render_WithFutureRetryBadge proves that), so the re-render is the
        // discriminating assertion; with any derived key the framework throws
        // "More than one sibling of element 'tr' has the same key value".
        var sharedRecipient = Guid.NewGuid();
        SetupFailures(
            Row(ContactChannelDto.Email, NotificationKindDto.Publish, attempt: 1, contactId: ContactId),
            Row(ContactChannelDto.Email, NotificationKindDto.Reminder, attempt: 3, contactId: ContactId),
            Row(ContactChannelDto.Email, NotificationKindDto.Publish, attempt: 1, contactId: ContactId, recipientId: sharedRecipient),
            Row(ContactChannelDto.Email, NotificationKindDto.Publish, attempt: 1, contactId: ContactId, recipientId: sharedRecipient));
        SetupContacts(new SubscribedContactDto(ContactId, ContactChannel.Email, "guardian@example.com", Role: null));

        var cut = Render<NotificationFailuresSection>(p =>
            p.Add(x => x.AssignmentId, AssignmentId).Add(x => x.CanHaveFailures, true));

        cut.WaitForAssertion(() => cut.FindAll("table tbody tr").Should().HaveCount(4));

        // Force a second render pass over the already-materialised keyed table.
        cut.Render();

        cut.FindAll("table tbody tr").Should().HaveCount(4,
            "all four rows survive a re-render — index keys keep the F1 duplicate-row case renderable");
        cut.Markup.Should().Contain(EnumHelper.GetDescription(NotificationKindDto.Publish));
        cut.Markup.Should().Contain(EnumHelper.GetDescription(NotificationKindDto.Reminder));
    }

    [TestMethod]
    public void FailureCountCallback_RaisedWithRowCount()
    {
        // WS-E2b / ar-18 (ar-17 follow-up F5): Detail badges its tab label from this
        // callback, so the section must report the row count on its own load.
        SetupFailures(
            Row(ContactChannelDto.Email, NotificationKindDto.Publish),
            Row(ContactChannelDto.SMS, NotificationKindDto.Reminder));
        SetupContacts();

        int? reported = null;
        var cut = Render<NotificationFailuresSection>(p => p
            .Add(x => x.AssignmentId, AssignmentId)
            .Add(x => x.CanHaveFailures, true)
            .Add(x => x.OnFailureCountLoaded, (int count) => reported = count));

        cut.WaitForAssertion(() => reported.Should().Be(2,
            "the count callback feeds the Detail tab badge"));
    }

    [TestMethod]
    public void FailureCountCallback_ZeroOnEmptyList()
    {
        // Zero is still reported (the badge renders the plain label for it) — the
        // distinction that matters is reported-0 versus never-reported.
        SetupFailures();
        SetupContacts();

        int? reported = null;
        var cut = Render<NotificationFailuresSection>(p => p
            .Add(x => x.AssignmentId, AssignmentId)
            .Add(x => x.CanHaveFailures, true)
            .Add(x => x.OnFailureCountLoaded, (int count) => reported = count));

        cut.WaitForAssertion(() => reported.Should().Be(0,
            "an empty list reports 0 so the tab badge stays plain"));
    }

    [TestMethod]
    public void FailureCountCallback_NotRaised_WhenLoadFails()
    {
        // The label must never advertise a count for a list the surface could not read
        // (ar-18 parameter contract).
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{AssignmentId}/notification-failures")
            .Respond(HttpStatusCode.InternalServerError);
        SetupContacts();

        int? reported = null;
        var cut = Render<NotificationFailuresSection>(p => p
            .Add(x => x.AssignmentId, AssignmentId)
            .Add(x => x.CanHaveFailures, true)
            .Add(x => x.OnFailureCountLoaded, (int count) => reported = count));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("intent-error"));
        reported.Should().BeNull(
            "a failed load must not report a count — the error bar is the only signal");
    }

    [TestMethod]
    public void BlankContactValue_FallsBackToMarkedShortId_NotAnEmptyCell()
    {
        // UI-tester P2: SubscribedContactDto.Value is a non-nullable string with no
        // content guarantee and ContactLabel had no IsNullOrWhiteSpace guard, so a
        // present-but-blank contact value rendered a visibly EMPTY Recipient cell on an
        // alerting surface — a hole that reads as a rendering bug. It must fall back to
        // the marked short id, exactly like an unmatched id.
        SetupFailures(Row(ContactChannelDto.Email, NotificationKindDto.Publish, contactId: ContactId));
        SetupContacts(new SubscribedContactDto(ContactId, ContactChannel.Email, "   ", Role: null));

        var cut = Render<NotificationFailuresSection>(p =>
            p.Add(x => x.AssignmentId, AssignmentId).Add(x => x.CanHaveFailures, true));

        cut.WaitForAssertion(() =>
        {
            var cell = cut.Find("table tbody tr td");
            cell.TextContent.Trim().Should().NotBeEmpty(
                "a blank contact value must never render an empty Recipient cell");
            cell.TextContent.Trim().Should().Be("#" + ContactId.ToString("N")[..8],
                "a blank value falls back to the marked short id");
        });
    }
}
