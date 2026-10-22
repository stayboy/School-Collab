using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The shared editor's live preview (redesign spec §3.3, verification row §8): the rendered subject and
/// body, an unknown tag left literal, and the degraded note whose failure can never block Save. The
/// behaviour came from the deleted page suite; it is asserted here through the one component Create and
/// Detail now share, so the two surfaces cannot drift.
/// </summary>
[TestClass]
public class TemplateEditorPreviewTests : BunitContext
{
    public TemplateEditorPreviewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private TemplateApiHandler Register() => TemplateTestServices.Register<TemplateEditor>(this);

    private IRenderedComponent<TemplateEditor> RenderEditor(Guid id) =>
        Render<TemplateEditor>(p => p
            .Add(x => x.Mode, TemplateEditorMode.Edit)
            .Add(x => x.Id, id));

    [TestMethod]
    public void Preview_ShowsTheRenderedPair_IncludingAnUnknownTagLiteral()
    {
        var handler = Register();
        var row = new TemplateRow { Kind = 0, Channel = 0, Subject = "AUTHORED", Body = "<p>{{Title}}</p>" };
        handler.Rows.Add(row);
        handler.PreviewJson = """{ "subject": "Rendered subject", "body": "<p>Hello {{NoSuchTag}}</p>" }""";

        var cut = RenderEditor(row.Id);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#template-preview-subject").TextContent.Trim().Should().Be("Rendered subject",
                "the preview renders through the Assignments host's own path");
            cut.Markup.Should().Contain("{{NoSuchTag}}",
                "an unknown tag stays literal in the preview (spec §3.3)");
        }, TimeSpan.FromSeconds(5));

        handler.Calls.Should().Contain(c =>
            c.Method == "POST" && c.Url == "/assignments/notification-templates/preview");
    }

    [TestMethod]
    public async Task PreviewFailure_ShowsTheNote_AndSaveStillWorks()
    {
        var handler = Register();
        var row = new TemplateRow { Kind = 0, Channel = 1, Body = "OLD" };
        handler.Rows.Add(row);
        handler.PreviewFails = true;

        var cut = RenderEditor(row.Id);

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("Preview unavailable", "a dead preview degrades to an inline note"),
            TimeSpan.FromSeconds(5));

        var body = cut.FindComponents<FluentTextArea>().Single(a => a.Instance.Id == "template-body-text");
        await cut.InvokeAsync(() => body.Instance.ValueChanged.InvokeAsync("STILL SAVES"));

        cut.Find("#template-save").Click();

        cut.WaitForAssertion(() =>
            handler.Calls.Should().Contain(c =>
                c.Method == "PUT" && c.Body!.Contains("STILL SAVES"),
                "the preview is an enrichment read — it can never block the save path"),
            TimeSpan.FromSeconds(5));
    }
}
