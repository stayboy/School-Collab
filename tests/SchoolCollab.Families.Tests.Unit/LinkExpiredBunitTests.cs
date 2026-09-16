using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Families.Components.Pages.DeepLink;

namespace SchoolCollab.Families.Tests.Unit;

/// <summary>
/// WS-E1 (ar-14-deep-links) — the friendly "link expired" page that the public /deeplink
/// landing redirects to for an expired / tampered / dark-launch-false token (the flow is a
/// 302 → 200 redirect to this page, not a 410). Renders under an anonymous principal (the
/// default bUnit principal) and shows plain, actionable copy — no stack / error chrome.
/// </summary>
[TestClass]
public class LinkExpiredBunitTests : BunitContext
{
    public LinkExpiredBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    [TestMethod]
    public void Renders_FriendlyExpiredMessage()
    {
        var cut = Render<LinkExpired>();

        cut.Markup.Should().Contain("This link has expired");
        cut.Markup.Should().Contain("Ask your school or your guardian for a fresh");
        cut.Markup.Should().Contain("contact your school office");
        // The expired-link page must not leak stack/error chrome.
        cut.Markup.Should().NotContain("Stack Trace");
        cut.Markup.Should().NotContain("Exception");
    }

    [TestMethod]
    public void Renders_GoToAssignments_Anchor_ToFamiliesRoot()
    {
        // P2 (ar-14): the expired page must offer a navigation affordance back to the
        // Families root, not trap the visitor on dead-end copy.
        var cut = Render<LinkExpired>();

        var anchor = cut.Find("[class*='deeplink-expired-anchor']");
        anchor.TextContent.Trim().Should().Be("Go to my assignments");
        anchor.GetAttribute("href").Should().Be("/ward");
    }
}
