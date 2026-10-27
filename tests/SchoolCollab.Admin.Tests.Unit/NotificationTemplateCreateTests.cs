using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The create page (redesign spec §3.4, naming spec §D1/D11), tested through the route host: the grade
/// deep link pre-fills a GradeLevel constraint (rendered as a chip with its resolved name), the two
/// dropdowns are editable, and ONE Save posts the whole draft — type, contact mode, TITLE, description,
/// body and the staged constraint set — then navigates to the new row's detail address.
/// <para>
/// A first write has no "why" (D11), so the create carries no reason and opens no dialog. A blank title
/// never reaches the server (D1): the editor refuses it before any write is dispatched.
/// </para>
/// </summary>
[TestClass]
public class NotificationTemplateCreateTests : BunitContext
{
    public NotificationTemplateCreateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private TemplateApiHandler Register() => TemplateTestServices.Register<Create>(this);

    /// <summary>The editor's live-preview endpoint — a <b>POST</b> that is not a store write.</summary>
    private const string PreviewPath = "/assignments/notification-templates/preview";

    private static bool IsStorePost((string Method, string Url, string? Body) call) =>
        call.Method == "POST"
        && call.Url.StartsWith(TemplateApiHandler.BasePath, StringComparison.OrdinalIgnoreCase);

    [TestMethod]
    public async Task Save_PostsTheWholeDraft_IncludingTheTitleAndDescription_WithNoReason()
    {
        // Arrange
        var handler = Register();
        var dialogs = TemplateDialogs.Register(this);
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/notification-templates/create?grade={TemplateApiHandler.GradeA}");

        var cut = Render<Create>();

        // The prefill renders as a chip carrying the LOOKED-UP grade name, not the raw id.
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Grade 2");
            cut.Markup.Should().Contain("Grade level");
        });

        await TypeTitleAsync(cut, "Grade 8 Science reminder");
        var description = cut.FindComponents<FluentTextArea>()
            .Single(a => a.Instance.Id == "template-description");
        await cut.InvokeAsync(() => description.Instance.ValueChanged.InvokeAsync("Sent the day before the lab."));

        var channel = cut
            .FindComponents<DropdownComponent<TemplateEditor.ChannelOption, NotificationChannel>>()
            .Single();
        await cut.InvokeAsync(() => channel.Instance.SelectedValueChanged.InvokeAsync(NotificationChannel.SMS));

        var body = cut.FindComponents<FluentTextArea>()
            .Single(a => a.Instance.Id == "template-body-text");
        await cut.InvokeAsync(() => body.Instance.ValueChanged.InvokeAsync("Hello {{Title}}"));

        // Act
        cut.Find("#template-save").Click();

        // Assert — a generous window: Save also refreshes the sibling/audit reads, and this suite runs
        // beside the landing/dialog suites (the icons-skill note on load-sensitive bUnit timeouts).
        cut.WaitForAssertion(() =>
        {
            // Filter to THIS endpoint, and report what else was posted if the expectation ever breaks.
            // `Single(c => c.Method == "POST")` over every call was the shape that reddened two of three
            // full-suite runs (issue #418): it couples this assertion — "the create posts the whole draft" —
            // to the absence of any other POST in the whole session. The other POST is now KNOWN and is
            // not unrelated: the editor's live preview is a POST to
            // /assignments/notification-templates/preview, queued behind a 300 ms debounce on every draft
            // edit and at editor init, so it lands whenever the run is slow enough to pass 300 ms between
            // the last edit and Save — which the whole-solution leg is and this class alone usually is not.
            // `DraftEdits_PostTheLivePreview_SoTheCreateCannotBeFoundByMethodAlone` pins that mechanism.
            var posts = handler.Calls.Where(c => c.Method == "POST").ToArray();
            var createPosts = posts.Where(IsStorePost).ToArray();

            createPosts.Should().ContainSingle(
                $"the create must post the draft exactly once. All POSTs this run: "
                + string.Join(" | ", posts.Select(p => p.Url)));

            var post = createPosts[0];
            post.Body.Should().Contain("\"channel\":1");
            post.Body.Should().Contain("Hello {{Title}}");
            post.Body.Should().Contain(TemplateApiHandler.GradeA);
            post.Body.Should().Contain("\"title\":\"Grade 8 Science reminder\"",
                "the title is required on create (naming spec D1)");
            post.Body.Should().Contain("\"description\":\"Sent the day before the lab.\"");
            post.Body.Should().NotContain("\"reason\":\"",
                "a first write has no why, so the create sends no reason (D11)");
            Services.GetRequiredService<NavigationManager>().Uri
                .Should().MatchRegex("/notification-templates/[0-9a-fA-F-]{36}$");
        }, TimeSpan.FromSeconds(5));

        dialogs.Opens.Should().BeEmpty("create is never interrupted by the reason dialog (D11)");
    }

    [TestMethod]
    public void Save_WithABlankTitle_ShowsTheError_AndNeverPosts()
    {
        // Arrange
        var handler = Register();
        var cut = Render<Create>();
        cut.WaitForAssertion(() => cut.FindAll("#template-save").Should().ContainSingle());

        // Act
        cut.Find("#template-save").Click();

        // Assert — the page refuses a draft it could not save before asking the operator for anything
        // or writing anything.
        cut.WaitForAssertion(() =>
            cut.Find("#template-editor-error").TextContent.Should().Contain("A title is required",
                "no template can be created nameless (naming spec D1/D2)"));

        // Nothing was dispatched to the store: the page refuses a draft it could not save before asking the
        // operator for anything or writing anything. Scoped to the store endpoint, because the editor's
        // live preview POSTs on its own timer (#418) and is not a write.
        handler.Calls.Should().NotContain(c => IsStorePost(c),
            "the page refuses a blank title before the write is dispatched");
    }

    /// <summary>
    /// <b>#418's cause, pinned.</b> The create page's Save is not the only POST in its session: the shared
    /// editor renders a live preview through <c>POST {PreviewPath}</c>, queued behind a 300 ms debounce by
    /// <c>QueuePreview</c> on every draft edit — and once at editor initialisation.
    ///
    /// <para>Therefore <c>handler.Calls.Single(c =&gt; c.Method == "POST")</c> never meant "the create": it
    /// meant "the create, provided no preview has fired yet". Whether the preview lands before or after
    /// Save is a function of wall-clock speed alone, which is exactly why the failure looked like a timing
    /// flake, appeared only in the whole-solution leg, and never reproduced in isolation. The fix is the
    /// one already in place — identify the create by its URL, not by its method — and this test is what
    /// keeps anyone from "simplifying" it back.</para>
    /// </summary>
    [TestMethod]
    public async Task DraftEdits_PostTheLivePreview_SoTheCreateCannotBeFoundByMethodAlone()
    {
        var handler = Register();
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/notification-templates/create?grade={TemplateApiHandler.GradeA}");

        var cut = Render<Create>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 2"));

        // A complete draft, so the Save below is dispatched rather than refused by validation.
        await TypeTitleAsync(cut, "Grade 8 Science reminder");
        var channel = cut
            .FindComponents<DropdownComponent<TemplateEditor.ChannelOption, NotificationChannel>>()
            .Single();
        await cut.InvokeAsync(() => channel.Instance.SelectedValueChanged.InvokeAsync(NotificationChannel.SMS));

        // One draft edit is enough to queue a preview; the editor also queues one at initialisation, so a
        // run slow enough to pass 300 ms between render and Save has already posted the preview.
        var body = cut.FindComponents<FluentTextArea>().Single(a => a.Instance.Id == "template-body-text");
        await cut.InvokeAsync(() => body.Instance.ValueChanged.InvokeAsync("Hello {{Title}}"));

        cut.WaitForAssertion(
            () => handler.Calls.Should().Contain(c => c.Method == "POST" && c.Url == PreviewPath,
                "the debounced live preview is a POST, and it is not a write to the template store"),
            TimeSpan.FromSeconds(5));

        handler.Calls.Should().NotContain(c => IsStorePost(c), "nothing has been saved yet");

        // Now save. Both POSTs exist, so a method-only filter is ambiguous by construction.
        cut.Find("#template-save").Click();

        cut.WaitForAssertion(
            () => handler.Calls.Where(IsStorePost).Should()
                .ContainSingle("one Save dispatches exactly one store write"),
            TimeSpan.FromSeconds(5));

        var posts = handler.Calls.Where(c => c.Method == "POST").ToArray();
        posts.Should().HaveCountGreaterThanOrEqualTo(2,
            "the run has already made a preview POST and the save POST — `Single(Method == \"POST\")` would "
            + "throw \"more than one matching element\", which is the #418 failure verbatim");
        posts.Where(p => !IsStorePost(p)).Should().OnlyContain(p => p.Url == PreviewPath,
            "and the only non-store POST is the preview endpoint");
    }

    [TestMethod]
    public void Create_StartsOnEmail_WithTheNamingRows_AndNoReasonFieldOnThePage()
    {
        // Arrange
        Register();

        // Act
        var cut = Render<Create>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            // Naming spec D1: the title (required) and the optional description sit above Template Type.
            cut.FindComponents<FluentTextInput>().Any(i => i.Instance.Id == "template-title").Should().BeTrue();
            cut.FindComponents<FluentTextArea>().Any(i => i.Instance.Id == "template-description").Should().BeTrue();

            // Email is the default mode, so the subject field exists and the rich body editor is the control.
            cut.FindComponents<FluentTextInput>().Any(i => i.Instance.Id == "template-subject").Should().BeTrue();
            cut.FindComponents<SchoolCollab.Admin.Shared.Components.RichText.RichInstructionEditor>()
                .Should().ContainSingle(e => e.Instance.Id == "template-body-editor");

            // D11: the audit reason left the page — it is asked for in a dialog, and never on create.
            cut.FindAll("#template-reason").Should().BeEmpty("the create page asks no why");
            cut.FindComponents<FluentTextArea>().Should().NotContain(a => a.Instance.Label == "Reason",
                "the audit reason is asked for in the shared dialog, never on the page (D11)");

            // No constraints yet: the panel says so instead of rendering an empty chip row.
            cut.Markup.Should().Contain("No constraints");
        });
    }

    /// <summary>Types the operator's title into the naming row (the draft is the control's own value).</summary>
    private static async Task TypeTitleAsync(IRenderedComponent<Create> cut, string title)
    {
        var input = cut.FindComponents<FluentTextInput>().Single(i => i.Instance.Id == "template-title");
        await cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(title));
    }
}
