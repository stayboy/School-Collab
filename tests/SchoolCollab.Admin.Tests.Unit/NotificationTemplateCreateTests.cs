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
/// The create page (redesign spec §3.4), tested through the route host: the grade deep link pre-fills a
/// GradeLevel constraint (rendered as a chip with its resolved name), the two dropdowns are editable,
/// and ONE Save posts the whole draft — kind, contact mode, body and the staged constraint set — then
/// navigates to the new row''s detail address.
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
    public async Task Save_PostsTheWholeDraft_IncludesThePrefilledGrade_AndNavigatesToTheNewRow()
    {
        var handler = Register();
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/notification-templates/create?grade={TemplateApiHandler.GradeA}");

        var cut = Render<Create>();

        // The prefill renders as a chip carrying the LOOKED-UP grade name, not the raw id.
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Grade 2");
            cut.Markup.Should().Contain("Grade level");
        });

        var channel = cut
            .FindComponents<DropdownComponent<TemplateEditor.ChannelOption, NotificationChannel>>()
            .Single();
        await cut.InvokeAsync(() => channel.Instance.SelectedValueChanged.InvokeAsync(NotificationChannel.SMS));

        var body = cut.FindComponents<FluentTextArea>()
            .Single(a => a.Instance.Id == "template-body-text");
        await cut.InvokeAsync(() => body.Instance.ValueChanged.InvokeAsync("Hello {{Title}}"));

        cut.Find("#template-save").Click();

        cut.WaitForAssertion(() =>
        {
            var post = handler.Calls.Single(c => c.Method == "POST");
            post.Body.Should().Contain("\"channel\":1");
            post.Body.Should().Contain("Hello {{Title}}");
            post.Body.Should().Contain(TemplateApiHandler.GradeA);
            Services.GetRequiredService<NavigationManager>().Uri
                .Should().MatchRegex("/notification-templates/[0-9a-fA-F-]{36}$");
        });
    }

    [TestMethod]
    public void Create_StartsOnEmailWithNoSubjectConstraintNoise()
    {
        Register();

        var cut = Render<Create>();

        cut.WaitForAssertion(() =>
        {
            // Email is the default mode, so the subject field exists and the rich body editor is the control.
            cut.FindComponents<FluentTextInput>().Any(i => i.Instance.Id == "template-subject").Should().BeTrue();
            cut.FindComponents<SchoolCollab.Admin.Shared.Components.RichText.RichInstructionEditor>()
                .Should().ContainSingle(e => e.Instance.Id == "template-body-editor");
            // No constraints yet: the panel says so instead of rendering an empty chip row.
            cut.Markup.Should().Contain("No constraints");
        });
    }
}