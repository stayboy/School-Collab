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
        var before = Services.GetRequiredService<NavigationManager>().Uri;

        var invoked = 0;
        var cut = Render<TestLandingPageOnCreate>(p => p
            .Add(x => x.CreateLabel, "+ New")
            .Add(x => x.CreateRoute, "/should-not-navigate")
            .Add(x => x.CreateEnabled, true)
            .Add(x => x.Items, Array.Empty<TestLandingPageOnCreate.Widget>())
            .Add(x => x.OnCreate, EventCallback.Factory.Create(this, () => invoked++)));

        var newButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Contains("+ New"));
        await newButton.ClickAsync(new MouseEventArgs());

        invoked.Should().Be(1, "OnCreate should fire once per click");
        Services.GetRequiredService<NavigationManager>().Uri
                .Should().Be(before, "OnCreate must take precedence over CreateRoute");
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
            .First(b => b.TextContent.Contains("+ New"));
        await newButton.ClickAsync(new MouseEventArgs());

        Services.GetRequiredService<NavigationManager>().Uri
                .Should().EndWith("/expected-route",
                    "without OnCreate, the landing falls back to Nav.NavigateTo(CreateRoute)");
    }

    [TestMethod]
    public void CreateEnabled_False_Hides_NewButton()
    {
        var cut = Render<TestLandingPageOnCreate>(p => p
            .Add(x => x.CreateLabel, "+ New")
            .Add(x => x.CreateRoute, "/x")
            .Add(x => x.CreateEnabled, false)
            .Add(x => x.Items, Array.Empty<TestLandingPageOnCreate.Widget>()));

        cut.FindAll("fluent-button")
            .Any(b => b.TextContent.Contains("+ New"))
            .Should().BeFalse("CreateEnabled=false hides the New button");
    }
}