using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Pins the kind banner (redesign spec §3.13): each notification kind carries a stable <c>data-kind</c>
/// (the grid's and detail header's shared seam), its own label, and the <b>agreed</b> semantic colour —
/// the expectation here comes from the spec's mapping, not from the component, so a silent re-colour
/// fails this test instead of passing it.
/// </summary>
[TestClass]
public class NotificationTemplateKindBannerTests : BunitContext
{
    public NotificationTemplateKindBannerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    [TestMethod]
    public void EveryKind_RendersItsLabelAndDataKind()
    {
        foreach (var kind in Enum.GetValues<NotificationTemplateKind>())
        {
            var cut = Render<NotificationTemplateKindBanner>(p => p.Add(x => x.Kind, kind));

            cut.Find(".kind-banner").GetAttribute("data-kind").Should().Be(kind.ToString());
            cut.Find(".kind-banner").TextContent.Should().Contain(kind.ToString());
        }
    }

    [TestMethod]
    [DataRow(NotificationTemplateKind.Publish, BadgeColor.Brand)]
    [DataRow(NotificationTemplateKind.Reminder, BadgeColor.Warning)]
    [DataRow(NotificationTemplateKind.Completion, BadgeColor.Success)]
    [DataRow(NotificationTemplateKind.Overdue, BadgeColor.Danger)]
    public void EachKind_UsesTheAgreedSemanticColour(NotificationTemplateKind kind, BadgeColor expected)
    {
        var cut = Render<NotificationTemplateKindBanner>(p => p.Add(x => x.Kind, kind));

        cut.FindComponent<FluentBadge>().Instance.Color.Should().Be(expected);
    }
}