using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Landing;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the additive <see cref="LandingPage.OnCreate"/> callback
/// (phase 1 of the landing-dialogs plan). The callback takes precedence over
/// the legacy <see cref="LandingPage.CreateRoute"/> navigation when supplied;
/// the legacy path is preserved as the fallback so every existing call site
/// keeps working.
/// </summary>
[TestClass]
public class LandingPageOnCreateTests : BunitContext
{
    public LandingPageOnCreateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    [TestMethod]
    public async Task OnCreate_Invokes_Callback_Instead_Of_Navigating()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var before = nav.Uri;

        var invoked = 0;
        var cut = Render<TestLandingPageOnCreate>(p => p
            .Add(x => x.CreateLabel, "+ New")
            .Add(x => x.CreateRoute, "/should-not-navigate")
            .Add(x => x.CreateEnabled, true)
            .Add(x => x.Items, Array.Empty<TestLandingPageOnCreate.Widget>())
            .Add(x => x.OnCreate, EventCallback.Factory.Create(this, () => invoked++)));

        // Q33(a): the New control is a FluentButton carrying OnClick — the rendered element that
        // owns the click; the pre-Q33(a) FluentLink affordance is gone.
        var newButton = cut.FindAll("fluent-button")
            .Single(b => b.TextContent.Contains("+ New"));
        cut.FindAll("fluent-link").Should().BeEmpty(
            "the New control no longer renders as the Q33(a)-superseded FluentLink");
        await cut.InvokeAsync(() => newButton.Click());

        invoked.Should().Be(1,
            "the New control invokes the host's OnCreate callback when one is supplied");
        nav.Uri.Should().Be(before,
            "OnCreate takes precedence: the click must not navigate to CreateRoute");
    }

    [TestMethod]
    public async Task OnCreate_NotSet_FallsBack_To_NavigateRoute()
    {
        var cut = Render<TestLandingPageOnCreate>(p => p
            .Add(x => x.CreateLabel, "+ New")
            .Add(x => x.CreateRoute, "/expected-route")
            .Add(x => x.CreateEnabled, true)
            .Add(x => x.Items, Array.Empty<TestLandingPageOnCreate.Widget>()));

        var newButton = cut.FindAll("fluent-button")
            .Single(b => b.TextContent.Contains("+ New"));
        await cut.InvokeAsync(() => newButton.Click());

        Services.GetRequiredService<NavigationManager>().Uri
            .Should().EndWith("/expected-route",
                "with no OnCreate supplied, the New button falls back to CreateRoute navigation");
    }

    [TestMethod]
    public void CreateEnabled_False_Hides_NewButton()
    {
        var cut = Render<TestLandingPageOnCreate>(p => p
            .Add(x => x.CreateLabel, "+ New")
            .Add(x => x.CreateRoute, "/x")
            .Add(x => x.CreateEnabled, false)
            .Add(x => x.Items, Array.Empty<TestLandingPageOnCreate.Widget>()));

        // The toolbar still renders, so an empty match below is the New control being hidden
        // rather than the whole action bar failing to render.
        cut.FindAll(".action-bar").Should().ContainSingle("the action bar still renders");

        // The New control is absent. The same selector + label predicate finds it in the two
        // OnCreate tests above (CreateEnabled=true), so this pin discriminates on CreateEnabled
        // instead of on a tag that no longer exists. Scoped to the action bar because the
        // empty-items info message bar renders a dismiss <fluent-button> of its own elsewhere.
        cut.FindAll(".action-bar fluent-button")
            .Should().NotContain(b => b.TextContent.Contains("+ New"),
                "CreateEnabled=false hides the New button");
    }
}