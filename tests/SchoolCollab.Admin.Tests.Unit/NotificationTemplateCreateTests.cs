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
            var post = handler.Calls.Single(c => c.Method == "POST");
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

        // Nothing was dispatched: the page refuses a draft it could not save before asking the operator
        // for anything or writing anything.
        handler.Calls.Should().NotContain(c => c.Method == "POST",
            "the page refuses a blank title before the write is dispatched");
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
