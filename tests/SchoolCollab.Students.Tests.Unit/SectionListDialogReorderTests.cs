using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Components.Students;
using System.Text.RegularExpressions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// AC11 (the Streams View-all shows icon-only move-up/move-down buttons in a
/// leading order column per row, and the buttons invoke the move callback and
/// re-render the moved order) and AC12 (a caller that supplies no reorder keys
/// renders exactly as before). Renders the dialog directly, like
/// <see cref="PeriodFormSequenceOptionsTests"/> renders its form.
/// </summary>
[TestClass]
public class SectionListDialogReorderTests : BunitContext
{
    public SectionListDialogReorderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed record Row(string Name);

    private static readonly Row[] ThreeRows = [new("Alpha"), new("Beta"), new("Gamma")];

    /// <summary>The dialog's inputs, seeded onto the typed <c>[Parameter]</c>s FluentUI 5.x
    /// spreads <c>DialogOptions.Parameters</c> onto by name (the dialog's key constants are
    /// <c>nameof(&lt;Parameter&gt;)</c>).</summary>
    private static void SeedBase(ComponentParameterCollectionBuilder<SectionListDialog<Row>> parameters)
    {
        parameters.Add(p => p.Items, ThreeRows);
        parameters.Add(p => p.TextSelector, new Func<Row, string?>(r => r.Name));
        parameters.Add(p => p.EmptyMessage, "No items yet.");
    }

    private IRenderedComponent<SectionListDialog<Row>> RenderDialog(
        Action<ComponentParameterCollectionBuilder<SectionListDialog<Row>>> seed) =>
        Render<SectionListDialog<Row>>(seed);

    private static IReadOnlyList<IElement> Buttons(
        IRenderedComponent<SectionListDialog<Row>> cut, string title) =>
        [.. cut.FindAll("fluent-button").Where(b => b.GetAttribute("title") == title)];

    private static string[] RenderedNames(IRenderedComponent<SectionListDialog<Row>> cut) =>
        [.. cut.FindAll(".section-list-dialog__name").Select(e => e.TextContent)];

    private static void SeedReorder(
        ComponentParameterCollectionBuilder<SectionListDialog<Row>> parameters,
        Action<Row> moved,
        Func<Task>? onChanged = null)
    {
        SeedBase(parameters);
        parameters.Add(p => p.MoveUp, new Func<Row, Task>(r => { moved(r); return Task.CompletedTask; }));
        parameters.Add(p => p.MoveDown, new Func<Row, Task>(r => { moved(r); return Task.CompletedTask; }));
        if (onChanged is not null) parameters.Add(p => p.OnChanged, onChanged);
    }

    // ── AC11 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void WithMoveCallbacks_RendersIconOnlyMoveButtonsAtTheStartOfEachRow()
    {
        var cut = RenderDialog(p => SeedReorder(p, _ => { }));

        var orderColumns = cut.FindAll(".section-list-dialog__order");
        orderColumns.Should().HaveCount(3, "each row leads with its reorder controls");
        foreach (var column in orderColumns)
        {
            column.ParentElement!.FirstElementChild.Should().BeSameAs(column,
                "the reorder controls sit at the START of the row, before the name");
        }
        Regex.IsMatch(cut.Find(".section-list-dialog").TextContent, @"\d+ of \d+").Should().BeFalse(
            "the controls are icon-only — no position text");

        Buttons(cut, "Move up").Should().HaveCount(3, "each row offers both moves");
        Buttons(cut, "Move down").Should().HaveCount(3);
    }

    [TestMethod]
    public void MoveDown_InvokesTheCallbackWithTheRow_AndSwapsTheRenderedOrder()
    {
        Row? moved = null;
        var cut = RenderDialog(p => SeedReorder(p, r => moved = r));

        // Row 1 (Alpha) moves down: the callback owns the API call, the dialog
        // swaps the two neighbours so the list reflects the move immediately.
        Buttons(cut, "Move down")[0].Click();

        moved.Should().NotBeNull();
        moved!.Name.Should().Be("Alpha", "the callback receives the row whose button was clicked");
        RenderedNames(cut).Should().Equal("Beta", "Alpha", "Gamma");
    }

    [TestMethod]
    public void MoveUp_InvokesTheCallbackWithTheRow_AndSwapsTheRenderedOrder()
    {
        Row? moved = null;
        var cut = RenderDialog(p => SeedReorder(p, r => moved = r));

        Buttons(cut, "Move up")[2].Click();

        moved!.Name.Should().Be("Gamma");
        RenderedNames(cut).Should().Equal("Alpha", "Gamma", "Beta");
    }

    [TestMethod]
    public void MoveButtonsAtTheEnds_AreDisabled()
    {
        var cut = RenderDialog(p => SeedReorder(p, _ => { }));

        Buttons(cut, "Move up")[0].HasAttribute("disabled").Should().BeTrue(
            "the first row cannot move up");
        Buttons(cut, "Move down")[2].HasAttribute("disabled").Should().BeTrue(
            "the last row cannot move down");
        Buttons(cut, "Move up")[2].HasAttribute("disabled").Should().BeFalse();
        Buttons(cut, "Move down")[0].HasAttribute("disabled").Should().BeFalse();
    }

    [TestMethod]
    public void OnChanged_RunsAfterASuccessfulMove()
    {
        var changed = 0;
        var cut = RenderDialog(p => SeedReorder(p, _ => { },
            onChanged: () => { changed++; return Task.CompletedTask; }));
        Buttons(cut, "Move down")[0].Click();

        changed.Should().Be(1, "the card behind the dialog refreshes to the new order");
    }

    // ── AC12 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void WithoutMoveCallbacks_RendersExactlyAsBefore_NoReorderControls()
    {
        var cut = RenderDialog(SeedBase);

        cut.FindAll(".section-list-dialog__order").Should().BeEmpty("no reorder keys — no order column at all");
        Regex.IsMatch(cut.Find(".section-list-dialog").TextContent, @"\d+ of \d+").Should().BeFalse();
        Buttons(cut, "Move up").Should().BeEmpty();
        Buttons(cut, "Move down").Should().BeEmpty();
        // A caller that wires no reorder keys keeps the original list order and layout.
        RenderedNames(cut).Should().Equal("Alpha", "Beta", "Gamma");
        cut.FindAll("fluent-message-bar").Should().BeEmpty();
    }

    [TestMethod]
    public void Dialog_ComposesTheSharedReadOnlyShell()
    {
        var cut = RenderDialog(SeedBase);

        cut.FindAll(".readonly-dialog").Should().ContainSingle(
            "the View-all's height, its single scroll region and the pinned Close row come from the shared shell");
        cut.Find(".readonly-dialog__scroll .section-list-dialog__list").Should().NotBeNull(
            "the list is the shell's scroll-region content and keeps its natural height (no self-cap)");
        cut.Find(".readonly-dialog__footer fluent-button").TextContent.Trim().Should().Be("Close",
            "the Close row is the shell's bottom-pinned action row");
    }
}
