using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Authoring;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The shared authoring chrome (ADR <c>adr-authoring-surface-pattern.md</c>, decision 1). The
/// assignment suite is the regression net for its own adoption; this suite pins the contract both
/// surfaces rely on — the crumb trail's shape, the pinned primary-action id, and the two slots.
/// </summary>
[TestClass]
public class AuthoringShellTests : BunitContext
{
    public AuthoringShellTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private static readonly AuthoringCrumb[] Trail =
    [
        new("Notification templates", "/notification-templates"),
        new("New template"),
    ];

    [TestMethod]
    public void CrumbTrail_RendersLinksThenTheCurrentPage()
    {
        var cut = Render<AuthoringShell>(p => p
            .Add(x => x.Crumbs, Trail)
            .Add(x => x.Title, "New template"));

        cut.Find("#authoring-breadcrumb").TextContent.Should().Contain("Notification templates");
        cut.FindAll("#authoring-breadcrumb fluent-link").Should().ContainSingle(
            "every crumb but the last is a link");
        cut.Find("#authoring-breadcrumb .authoring-crumb-current").TextContent.Trim()
            .Should().Be("New template");
        cut.FindAll("#authoring-breadcrumb .authoring-crumb-separator").Should().ContainSingle();
    }

    [TestMethod]
    public void PrimaryAction_UsesThePagesPinnedId_AndRaisesItsCallback()
    {
        var clicks = 0;
        var cut = Render<AuthoringShell>(p => p
            .Add(x => x.Crumbs, Trail)
            .Add(x => x.Title, "Template")
            .Add(x => x.PrimaryId, "template-save")
            .Add(x => x.PrimaryLabel, "Save")
            .Add(x => x.OnPrimaryAction, () => clicks++));

        cut.Find("#template-save").Click();

        clicks.Should().Be(1);
        cut.Find("#template-save").TextContent.Trim().Should().Be("Save");
    }

    [TestMethod]
    public void NoPrimaryLabel_RendersNoPrimaryButton()
    {
        var cut = Render<AuthoringShell>(p => p
            .Add(x => x.Crumbs, Trail)
            .Add(x => x.Title, "Template"));

        cut.FindAll("#authoring-primary-action").Should().BeEmpty(
            "a loading or read-only-now view shows no primary action");
    }

    [TestMethod]
    public void Slots_RenderBadgeLeadingAndSecondaryContent()
    {
        var cut = Render<AuthoringShell>(p => p
            .Add(x => x.Crumbs, Trail)
            .Add(x => x.Title, "Template")
            .Add(x => x.Badge, (RenderFragment)(b => b.AddMarkupContent(0, "<span id=\"shell-badge\">System</span>")))
            .Add(x => x.LeadingActions, (RenderFragment)(b => b.AddMarkupContent(0, "<span id=\"shell-leading\">reason</span>")))
            .Add(x => x.SecondaryActions, (RenderFragment)(b => b.AddMarkupContent(0, "<span id=\"shell-secondary\">more</span>"))));

        cut.Find("#shell-badge").Should().NotBeNull();
        cut.Find("#shell-leading").Should().NotBeNull();
        cut.Find("#shell-secondary").Should().NotBeNull();
    }
}
