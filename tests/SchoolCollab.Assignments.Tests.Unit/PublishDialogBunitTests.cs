using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round notification-templates-b2b (spec §3.7 picker + stamps, §3.8 medium) — the publish dialog's
/// medium radio and per-slot template pickers, rendered DIRECTLY through the real dialog shell (the
/// <see cref="QuestionEditDialogBunitTests"/> harness), so the pre-select, the rendered option order
/// and the fail-open branch are all exercised as markup.
///
/// <para><b>Where the pre-select rule lives:</b> <c>ContractTypes.cs:693-696</c> pins the client's
/// source as <c>stored stamp ?? first applicable candidate</c>. The stored-stamp case below is the
/// discriminating one: pre-fix (no stamp handling) the dialog would always land on the first
/// candidate and republish would silently overwrite the author's confirmed choice.</para>
/// </summary>
[TestClass]
public class PublishDialogBunitTests : BunitContext
{
    private static readonly Guid EmailDefault = Guid.Parse("e0000000-0000-0000-0000-00000000000e");
    private static readonly Guid EmailGrade = Guid.Parse("e0000000-0000-0000-0000-000000000066");
    private static readonly Guid EmailTwoDim = Guid.Parse("e0000000-0000-0000-0000-000000000077");
    private static readonly Guid EmailStale = Guid.Parse("e0000000-0000-0000-0000-0000000000aa");
    private static readonly Guid SmsDefault = Guid.Parse("e0000000-0000-0000-0000-000000000055");

    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public PublishDialogBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    private static PublishTemplateCandidateDto Candidate(
        Guid id, bool isDefault = false, params (string Dimension, string Value)[] constraints) =>
        new(id,
            constraints.Select(c => new PublishTemplateConstraintDto(c.Dimension, c.Value)).ToList(),
            isDefault);

    private static PublishChannelOptionsDto Slot(
        ContactChannelDto channel, IReadOnlyList<PublishTemplateCandidateDto> templates) =>
        new(NotificationKindDto.Publish, channel, null, null, [], templates);

    /// <summary>
    /// The server's shipped shape (the <c>GetPublishOptionsHandler</c> order): each slot's candidates
    /// arrive <b>most specific first</b>, so the FIRST entry is neither the tenant default nor the
    /// most-recently-created row. A client that re-sorted by constraint count, by id, or default-first
    /// would change both the pre-select and the rendered order.
    /// </summary>
    private static PublishOptionsDto Options(
        Guid? storedEmailStamp = null,
        Guid? storedSmsStamp = null,
        IReadOnlyList<ContactChannelDto>? blocked = null,
        IReadOnlyList<PublishTemplateCandidateDto>? emailTemplates = null,
        IReadOnlyList<PublishTemplateCandidateDto>? smsTemplates = null) =>
        new(
            [
                Slot(ContactChannelDto.Email, emailTemplates ?? MostSpecificFirstEmail()),
                Slot(ContactChannelDto.SMS, smsTemplates ?? DefaultSmsOnly()),            ],
            [ContactChannelDto.Email, ContactChannelDto.SMS],
            blocked ?? [],
            storedEmailStamp,
            storedSmsStamp);

    private static PublishTemplateCandidateDto[] MostSpecificFirstEmail() =>
    [
        Candidate(EmailTwoDim, constraints: [("GradeLevel", "Grade 3"), ("AssignmentType", "Digital")]),
        Candidate(EmailGrade, constraints: [("GradeLevel", "Grade 3")]),
        Candidate(EmailDefault, isDefault: true),
    ];

    private static PublishTemplateCandidateDto[] DefaultSmsOnly() =>
        [Candidate(SmsDefault, isDefault: true)];

    private static PublishFormModel Model(PublishOptionsDto? options) =>
        new(
            [
                new SubscribedContactDto(Guid.NewGuid(), ContactChannel.Email, "guardian@example.org", GuardianRole.Primary),
            ],
            "Audience: Grade 3 — All students",
            MandatoryReview: false,
            options);

    private (IRenderedComponent<FluentDialogProvider> Provider, Task<PublishResult?> Closed) Open(PublishFormModel model)
    {
        var provider = Render<FluentDialogProvider>();
        var closed = DialogService.ShowShellDialogAsync<PublishDialog, PublishFormModel, PublishResult>(
            model, title: "Publish assignment", size: DialogWidth.Medium);
        provider.WaitForAssertion(() => provider.Find("form"));
        return (provider, closed);
    }

    /// <summary>The Email slot's dropdown, addressed by its wrapper so the SMS slot can never answer
    /// in its place.</summary>
    private static IRenderedComponent<DropdownComponent<PublishTemplateCandidateDto, Guid?>> EmailPicker(
        IRenderedComponent<FluentDialogProvider> provider) =>
        provider.FindComponents<DropdownComponent<PublishTemplateCandidateDto, Guid?>>()
            .Single(c => c.Instance.Label == "Email template");

    private static IRenderedComponent<DropdownComponent<PublishTemplateCandidateDto, Guid?>> SmsPicker(
        IRenderedComponent<FluentDialogProvider> provider) =>
        provider.FindComponents<DropdownComponent<PublishTemplateCandidateDto, Guid?>>()
            .Single(c => c.Instance.Label == "SMS template");

    /// <summary>The option texts a picker renders, in DOM order — the rendered-order observable.</summary>
    private static List<string> RenderOptionTexts(
        IRenderedComponent<FluentDialogProvider> provider, string slotWrapperId) =>
        provider.FindAll($"#{slotWrapperId} fluent-option")
            .Select(o => o.TextContent.Trim())
            .ToList();

    /// <summary>The option text the picker currently renders as selected (FluentUI 5.1 writes no
    /// <c>current-value</c>; the selection is the one option carrying <c>selected</c>).</summary>
    private static string? SelectedOptionText(
        IRenderedComponent<FluentDialogProvider> provider, string slotWrapperId) =>
        provider.FindAll($"#{slotWrapperId} fluent-option")
            .SingleOrDefault(o => o.HasAttribute("selected"))
            ?.TextContent.Trim();

    /// <summary>Asserts a slot is pre-selected on <paramref name="templateId"/> AND that the dropdown
    /// displays it (the option rendered as <c>selected</c> carries that candidate's own summary).</summary>
    private static void AssertPreSelected(
        IRenderedComponent<FluentDialogProvider> provider,
        IRenderedComponent<DropdownComponent<PublishTemplateCandidateDto, Guid?>> picker,
        string slotWrapperId,
        Guid templateId,
        string optionText)
    {
        picker.Instance.SelectedValue.Should().Be(templateId);
        SelectedOptionText(provider, slotWrapperId).Should().Be(optionText,
            "the pre-selected candidate is the one the picker renders as selected");
    }

    // ── pre-select ───────────────────────────────────────────────────────────

    /// <summary>No stored stamp ⇒ the FIRST candidate the server offered is pre-selected (the
    /// most-specific applicable one), never the tenant default and never the last entry.</summary>
    [TestMethod]
    public void NoStoredStamp_PreSelectsTheFirstCandidate()
    {
        var (provider, _) = Open(Model(Options()));

        AssertPreSelected(provider, EmailPicker(provider), "cq-publish-template-email", EmailTwoDim,
            "GradeLevel: Grade 3 · AssignmentType: Digital");
    }

    /// <summary>The P1 case: a stored stamp that is NOT the first candidate pre-selects the stored
    /// one, so unpublish → republish reuses the author's confirmed choice instead of silently
    /// overwriting it with "most specific".</summary>
    [TestMethod]
    public void StoredStamp_ThatIsNotTheFirstCandidate_IsPreSelected()
    {
        var (provider, _) = Open(Model(Options(storedEmailStamp: EmailGrade)));

        AssertPreSelected(provider, EmailPicker(provider), "cq-publish-template-email", EmailGrade,
            "GradeLevel: Grade 3");
    }

    /// <summary>A stale stamp (the template was deleted, or no longer applies) is absent from the
    /// server's candidate list: the picker falls back to the first entry and the stale id is NEVER
    /// sent (an explicit non-live stamp is a 400 on the publish route).</summary>
    [TestMethod]
    public async Task StaleStamp_FallsBackToTheFirstEntry_AndIsNeverSent()
    {
        var (provider, closed) = Open(Model(Options(storedEmailStamp: EmailStale)));

        AssertPreSelected(provider, EmailPicker(provider), "cq-publish-template-email", EmailTwoDim,
            "GradeLevel: Grade 3 · AssignmentType: Digital");

        provider.Find("form").Submit();
        var result = await closed;

        result.Should().NotBeNull();
        result!.PublishEmailTemplateId.Should().Be(EmailTwoDim,
            "the stale id must never reach the publish request");
    }

    // ── rendered order ───────────────────────────────────────────────────────

    /// <summary>The rendered option order equals the server's order VERBATIM — the direct kill for a
    /// client-side re-sort. The pre-select alone cannot pin this: under Q67's default-first tie an
    /// "IsDefault first" re-sort can leave the winner looking identical, so the order itself is the
    /// assertion (the fixture deliberately does NOT put the default first).</summary>
    [TestMethod]
    public void RenderedOptionOrder_EqualsTheServersOrder_Verbatim()
    {
        var (provider, _) = Open(Model(Options()));

        RenderOptionTexts(provider, "cq-publish-template-email").Should().Equal(
            new[]
            {
                "GradeLevel: Grade 3 · AssignmentType: Digital",
                "GradeLevel: Grade 3",
                "Tenant default",
            },
            "the candidates render in the received (evaluator) order — most specific first, the tenant "
            + "default last; any re-sort in the component would undo the Q67 tie rule");
    }

    /// <summary>The constraint summary and the <c>Tenant default</c> marker are what the picker shows
    /// per candidate (spec §3.7).</summary>
    [TestMethod]
    public void OptionText_CarriesTheConstraintSummary_AndTheTenantDefaultMarker()
    {
        var (provider, _) = Open(Model(Options()));

        var options = provider.FindAll("#cq-publish-template-email fluent-option")
            .Select(o => o.TextContent.Trim()).ToList();

        options[0].Should().Contain("GradeLevel: Grade 3").And.Contain("AssignmentType: Digital");
        options[0].Should().NotContain("Tenant default", "a constrained candidate is not the default");
        options[2].Should().Be("Tenant default", "the unconstrained tenant row is marked as such");
    }

    // ── empty candidates ─────────────────────────────────────────────────────

    /// <summary>A successful read with an empty candidate list (no templates configured at all) shows
    /// the read-only note and stamps nothing — the server's built-in default renders.</summary>
    [TestMethod]
    public async Task EmptyTemplates_ShowTheReadOnlyNote_AndStampNothing()
    {
        var options = Options(emailTemplates: [], smsTemplates: []);
        var (provider, closed) = Open(Model(options));

        provider.Find("#cq-publish-template-email-empty").TextContent.Should()
            .Contain("no templates configured").And.Contain("built-in default");
        provider.FindComponents<DropdownComponent<PublishTemplateCandidateDto, Guid?>>().Should().BeEmpty(
            "there is nothing to pick from — the slot renders a note, not an empty dropdown");

        provider.Find("form").Submit();
        var result = await closed;

        result.Should().NotBeNull();
        result!.PublishEmailTemplateId.Should().BeNull("nothing is stamped when no template is configured");
        result.PublishSmsTemplateId.Should().BeNull();
    }

    // ── fail-open ────────────────────────────────────────────────────────────

    /// <summary>The load-bearing outage case: an options read that failed renders the warning bar and
    /// NO pickers — never the "no templates configured" note, which belongs only to a SUCCESSFUL read
    /// with an empty list. Publishing still proceeds, per policy, with no stamps.</summary>
    [TestMethod]
    public async Task OptionsNull_RendersTheWarningBar_AndNoPickers()
    {
        var (provider, closed) = Open(Model(null));

        provider.Find("#cq-publish-options-unavailable").TextContent.Should()
            .Contain("Template options are unavailable right now")
            .And.Contain("policy defaults");
        provider.FindAll("#cq-publish-template-email").Should().BeEmpty(
            "an outage must not look like a configured dialog with pickers");
        provider.FindAll("#cq-publish-template-sms").Should().BeEmpty();
        provider.Markup.Should().NotContainEquivalentOf("no templates configured",
            "that note is reserved for a successful read with an empty candidate list");

        provider.Find("form").Submit();
        var result = await closed;

        result.Should().NotBeNull();
        result!.Channels.Should().Be(PublishChannel.PerPolicy, "an outage publishes per policy");
        result.PublishEmailTemplateId.Should().BeNull();
        result.PublishSmsTemplateId.Should().BeNull();
    }

    // ── medium ───────────────────────────────────────────────────────────────

    /// <summary>A tenant-blocked channel is ANNOTATED, never disabled: all three medium states stay
    /// selectable, because the server's blocked channels still win and a disabled option would read
    /// as "not offered" instead of "offered, but the policy overrides it".</summary>
    [TestMethod]
    public void BlockedChannel_IsAnnotated_WhileTheOptionStaysSelectable()
    {
        var (provider, _) = Open(Model(Options(blocked: [ContactChannelDto.Email])));

        provider.Find("#cq-publish-medium-email-blocked").TextContent.Should()
            .Contain("blocked by policy").And.Contain("stays blocked");

        provider.FindComponent<FluentRadioGroup<PublishChannel>>().Instance.Disabled.Should().NotBe(true,
            "one blocked channel is not 'the sendout reaches no one'");

        provider.FindAll("label")
            .Single(l => l.TextContent.Contains("Email only"))
            .TextContent.Should().Contain("blocked by policy",
                "the annotation sits in the option's own label — the option is annotated, not disabled");
    }

    /// <summary>Both channel groups blocked ⇒ the radio group is disabled, with a footer explaining
    /// that publishing reaches no one by either medium.</summary>
    [TestMethod]
    public void BothChannelGroupsBlocked_DisablesTheRadioGroup_WithTheFooterExplanation()
    {
        var (provider, _) = Open(Model(Options(
            blocked: [ContactChannelDto.Email, ContactChannelDto.SMS, ContactChannelDto.WhatsApp])));

        provider.FindComponent<FluentRadioGroup<PublishChannel>>().Instance.Disabled.Should()
            .Be(true, "no medium can deliver, so the choice is not offered");
        provider.Find("#cq-publish-medium-email-blocked").Should().NotBeNull();
        provider.Find("#cq-publish-medium-sms-blocked").Should().NotBeNull();
        provider.Find("#cq-publish-medium-all-blocked").TextContent.Should()
            .Contain("reaches").And.Contain("no one");
    }

    /// <summary>The chosen medium excludes a slot ⇒ that picker is disabled and KEEPS its selection
    /// (the author's pick is not thrown away by flipping the medium back and forth).</summary>
    [TestMethod]
    public async Task MediumExcludedSlot_IsDisabled_AndKeepsItsSelection()
    {
        var options = Options(storedEmailStamp: EmailGrade);
        var (provider, closed) = Open(Model(options));

        EmailPicker(provider).Instance.SelectedValue.Should().Be(EmailGrade);

        // Choose "SMS only": the Email slot is excluded but keeps the stored pick.
        await provider.InvokeAsync(() =>
            provider.FindComponent<FluentRadioGroup<PublishChannel>>().Instance.ValueChanged
                .InvokeAsync(PublishChannel.SmsOnly));

        EmailPicker(provider).Instance.Disabled.Should().BeTrue(
            "the medium excludes the email medium, so the pick has no effect this sendout");
        EmailPicker(provider).Instance.SelectedValue.Should().Be(EmailGrade,
            "a disabled slot keeps its selection — flipping the medium back must not lose it");
        SmsPicker(provider).Instance.Disabled.Should().BeFalse();
        provider.Find("#cq-publish-template-email-excluded").Should().NotBeNull(
            "a disabled control states why (the repo's disabled-with-reason convention)");

        provider.Find("form").Submit();
        var result = await closed;

        result.Should().NotBeNull();
        result!.Channels.Should().Be(PublishChannel.SmsOnly);
    }

    // ── result ───────────────────────────────────────────────────────────────

    /// <summary>The confirmed result carries the picked ids and the medium — what the page forwards to
    /// the publish request.</summary>
    [TestMethod]
    public async Task ConfirmedResult_CarriesThePicks_AndTheMedium()
    {
        var options = Options(storedSmsStamp: SmsDefault);
        var (provider, closed) = Open(Model(options));

        await provider.InvokeAsync(() =>
            provider.FindComponent<FluentRadioGroup<PublishChannel>>().Instance.ValueChanged
                .InvokeAsync(PublishChannel.EmailOnly));

        provider.Find("form").Submit();
        var result = await closed;

        result.Should().NotBeNull();
        result!.PublishEmailTemplateId.Should().Be(EmailTwoDim, "the first candidate is pre-selected and confirmed");
        result.PublishSmsTemplateId.Should().Be(SmsDefault, "the stored SMS stamp survives the medium change");
        result.Channels.Should().Be(PublishChannel.EmailOnly);
        result.ContactIds.Should().BeNull("no contact limit was set");
    }

    /// <summary>Regression guard: cancelling returns null and stamps nothing.</summary>
    [TestMethod]
    public async Task Cancel_ReturnsNull()
    {
        var (provider, closed) = Open(Model(Options()));

        provider.FindAll("fluent-button")
            .Single(b => b.TextContent.Contains("Cancel"))
            .Click();

        var result = await closed;
        result.Should().BeNull("a cancelled dialog returns null, so the page never publishes");
    }
}
