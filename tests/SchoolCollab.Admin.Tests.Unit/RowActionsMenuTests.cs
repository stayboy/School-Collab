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
