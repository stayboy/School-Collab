using Bunit;
using FluentAssertions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Constants;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the shared <see cref="RowActionsMenu"/> kebab component.
/// Covers the per-row rendering contract (0 / 1 / 2+ actions) and the
/// grid-level <see cref="RowActionsMenu.ForceKebab"/> consistency flag
/// (repo convention: when any row qualifies for the kebab, every row with at
/// least one action renders the kebab). UseMenuService="false" so the menu
/// items render inline and are assertable in the markup.
/// </summary>
[TestClass]
public class RowActionsMenuTests : BunitContext
{
    public RowActionsMenuTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private static RowAction Edit() => RowAction.Callback("Edit", () => { }, FluentIcons.Edit);

    private static RowAction Delete() =>
        RowAction.Callback("Delete", () => { }, FluentIcons.Delete, destructive: true);

    [TestMethod]
    public void ZeroActions_RendersNothing()
    {
        var cut = Render<RowActionsMenu>(p => p
            .Add(x => x.Actions, Array.Empty<RowAction>())
            .Add(x => x.UseMenuService, false));

        cut.Markup.Should().NotContain("fluent-button", "no actions means no trigger");
    }

    [TestMethod]
    public void SingleAction_WithoutForceKebab_RendersLabeledButton()
    {
        var cut = Render<RowActionsMenu>(p => p
            .Add(x => x.Actions, new[] { Edit() })
            .Add(x => x.UseMenuService, false));

        cut.Markup.Should().Contain(">Edit</fluent-button>", "a lone action renders a labeled button");
        cut.Markup.Should().NotContain("row-actions-btn", "no kebab trigger for a single action");
    }

    [TestMethod]
    public void SingleAction_WithForceKebab_RendersKebab()
    {
        var cut = Render<RowActionsMenu>(p => p
            .Add(x => x.Actions, new[] { Edit() })
            .Add(x => x.UseMenuService, false)
            .Add(x => x.ForceKebab, true));

        cut.Markup.Should().Contain("row-actions-btn", "ForceKebab renders the kebab trigger");
        cut.Markup.Should().NotContain(">Edit</fluent-button>", "no lone labeled button when the kebab is forced");
    }

    [TestMethod]
    public void TwoActions_RendersKebab()
    {
        var cut = Render<RowActionsMenu>(p => p
            .Add(x => x.Actions, new[] { Edit(), Delete() })
            .Add(x => x.UseMenuService, false));

        cut.Markup.Should().Contain("row-actions-btn", "2+ actions render the kebab trigger");
    }

    /// <summary>A disabled action that names its reason renders that reason as its accessible
    /// description — on the single-action labeled button AND on the kebab item — so a greyed-out
    /// action explains itself instead of being a dead end (F7).
    /// <para>Discriminating: against the pre-F7 component the reason was never rendered (the
    /// property does not exist and neither rendering read it).</para></summary>
    [TestMethod]
    public void DisabledActionWithReason_RendersTheReasonAsItsDescription()
    {
        var reason = "Not available for this combination — add questions by hand.";

        // Single action → the labeled button.
        var button = Render<RowActionsMenu>(p => p
            .Add(x => x.Actions, new[]
            {
                RowAction.Callback("Generate questions", () => { }, FluentIcons.Bot,
                    disabled: true, disabledReason: reason)
            })
            .Add(x => x.UseMenuService, false));

        button.Find("fluent-button").GetAttribute("title").Should().Be(reason,
            "F7: the disabled button's title/accessible description carries the reason");

        // Two actions → the kebab, whose item must carry it too. The items only render once the
        // trigger is opened (the shared menu renders through FluentMenu).
        var kebab = Render<RowActionsMenu>(p => p
            .Add(x => x.Actions, new[]
            {
                Edit(),
                RowAction.Navigate("Generate questions", "#questions", FluentIcons.Bot,
                    disabled: true, disabledReason: reason)
            })
            .Add(x => x.UseMenuService, false));
        kebab.Find("fluent-button[title='More actions']").Click();

        kebab.WaitForAssertion(() => kebab.FindAll("fluent-menu-item")
            .Single(i => i.TextContent.Trim() == "Generate questions")
            .GetAttribute("title").Should().Be(reason,
                "F7: the kebab item's title/accessible description carries the reason too"));
    }

    /// <summary>F7's other half: an action with NO reason invents none. The enabled/disabled
    /// single-action button keeps the label as its title and renders no description an author could
    /// mistake for an explanation, and the kebab item renders no title attribute at all.
    /// <para>Discriminating only for the absent-reason half: the pre-F7 component also rendered no
    /// description, so this test guards the new property against inventing text rather than proving
    /// the fix; the reason-rendering test above is the discriminating one.</para></summary>
    [TestMethod]
    public void ActionWithoutReason_KeepsItsLabelAndInventsNoDescription()
    {
        var enabled = Render<RowActionsMenu>(p => p
            .Add(x => x.Actions, new[] { Edit() })
            .Add(x => x.UseMenuService, false));

        enabled.Find("fluent-button").GetAttribute("title").Should().Be("Edit",
            "an action with no reason is unaffected by F7");

        var disabled = Render<RowActionsMenu>(p => p
            .Add(x => x.Actions, new[] { RowAction.Callback("Edit", () => { }, FluentIcons.Edit, disabled: true) })
            .Add(x => x.UseMenuService, false));

        disabled.Find("fluent-button").GetAttribute("title").Should().Be("Edit",
            "a disabled action with no reason must not have one invented for it");

        var kebab = Render<RowActionsMenu>(p => p
            .Add(x => x.Actions, new[]
            {
                Edit(),
                RowAction.Callback("Delete", () => { }, FluentIcons.Delete, disabled: true)
            })
            .Add(x => x.UseMenuService, false));
        kebab.Find("fluent-button[title='More actions']").Click();

        kebab.WaitForAssertion(() => kebab.FindAll("fluent-menu-item")
            .Single(i => i.TextContent.Trim() == "Delete")
            .GetAttribute("title").Should().BeNull("no reason, no title attribute"));
    }

    [TestMethod]
    public void HasKebabActions_True_OnlyForTwoOrMoreNonSeparators()
    {
        RowActionsMenu.HasKebabActions(Array.Empty<RowAction>()).Should().BeFalse();
        RowActionsMenu.HasKebabActions(new[] { Edit() }).Should().BeFalse();
        RowActionsMenu.HasKebabActions(new[] { Edit(), Delete() }).Should().BeTrue();
        // Separators do not count toward the kebab threshold.
        RowActionsMenu.HasKebabActions(new[] { Edit(), RowAction.Separator() }).Should().BeFalse();
    }
}
