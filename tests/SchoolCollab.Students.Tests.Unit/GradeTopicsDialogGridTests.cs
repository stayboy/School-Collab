using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Students.Application.Components.Students;
using System.Reflection;
using System.Text.RegularExpressions;
using GradeTopicCurriculumDto = SchoolCollab.Students.Application.Services.GradeTopicCurriculumDto;
using TopicDto = SchoolCollab.Students.Core.DTOs.TopicDto;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// AC13 (the Subjects View-all is a FluentDataGrid with Subject / Strands /
/// Lessons / Exceptions columns whose Actions column holds ONE control per row —
/// the shared kebab (<c>RowActionsMenu</c>) carrying Edit / Strands / Teachers /
/// Enrollment exceptions / Remove, with the destructive Remove confirmed by that
/// shared menu, not by the dialog), AC14 (the
/// exception counts come from the existing <c>ExceptionCountsByTopic</c> map and
/// a zero renders no badge) and AC15 (the reorder buttons invoke the MoveUp /
/// MoveDown callbacks with the row carrying its bridge assignment id), and the
/// owner's second 2026-09-30 revision (the Actions column is kebab-only: no
/// standalone Edit subject / Remove subject icon buttons duplicate what the
/// kebab already carries).
/// </summary>
[TestClass]
public class GradeTopicsDialogGridTests : BunitContext
{
    private static readonly Guid MathTopicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EngTopicId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MathAssignmentId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid EngAssignmentId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    public GradeTopicsDialogGridTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private static GradeTopicCurriculumDto[] TwoSubjects() =>
    [
        new(MathAssignmentId, MathTopicId, "Mathematics", "MATH", 2, 3),
        new(EngAssignmentId, EngTopicId, "English", "ENG", 1, 0),
    ];

    private static DialogParameters Content(
        Func<GradeTopicCurriculumDto, Task>? moveUp = null,
        Func<GradeTopicCurriculumDto, Task>? moveDown = null,
        Func<GradeTopicCurriculumDto, Task>? edit = null,
        Func<Guid, Task>? remove = null)
    {
        var content = new DialogParameters
        {
            { GradeTopicsDialog.TopicsKey, TwoSubjects() },
            { GradeTopicsDialog.UnassignedTopicsKey, Array.Empty<TopicDto>() },
            { GradeTopicsDialog.ExceptionCountsByTopicKey, new Dictionary<Guid, int> { [MathTopicId] = 2 } },
        };
        if (moveUp is not null) content[GradeTopicsDialog.MoveUpKey] = moveUp;
        if (moveDown is not null) content[GradeTopicsDialog.MoveDownKey] = moveDown;
        if (edit is not null) content[GradeTopicsDialog.EditKey] = edit;
        if (remove is not null) content[GradeTopicsDialog.RemoveKey] = remove;
        return content;
    }

    private IRenderedComponent<GradeTopicsDialog> RenderDialog(DialogParameters content) =>
        Render<GradeTopicsDialog>(p => p.Add(x => x.Content, content));

    /// <summary>The body cell holding each row's <c>.topic-dialog__order</c> controls.
    /// The header row has no <c>td</c>, so every element here belongs to a subject row.</summary>
    private static IReadOnlyList<IElement> OrderCells(IRenderedComponent<GradeTopicsDialog> cut) =>
        [.. cut.FindAll("td").Where(td => td.QuerySelector(".topic-dialog__order") is not null)];

    /// <summary>The body cell holding each row's Actions controls — the row's kebab
    /// with its inline menu (<c>UseMenuService=false</c> on this dialog).</summary>
    private static IReadOnlyList<IElement> ActionCells(IRenderedComponent<GradeTopicsDialog> cut) =>
        [.. cut.FindAll("td").Where(td => td.QuerySelector(".topic-dialog__actions") is not null)];

    /// <summary>Opens row <paramref name="row"/>'s kebab menu and returns that row's
    /// Actions cell RE-QUERIED after the click (the click re-renders the row).
    /// <c>UseMenuService=false</c> on this dialog, so the menu's items render inline
    /// inside the cell and are assertable.</summary>
    private static IElement OpenKebab(IRenderedComponent<GradeTopicsDialog> cut, int row)
    {
        ActionCells(cut)[row]
            .QuerySelector("fluent-button[title='Topic actions']")!.Click();
        return ActionCells(cut)[row];
    }

    private static IReadOnlyList<IElement> KebabItems(IElement actionsCell) =>
        [.. actionsCell.QuerySelectorAll("fluent-menu-item")];

    private static string LabelOf(IElement menuItem) =>
        menuItem.GetAttribute("label") ?? menuItem.TextContent.Trim();

    /// <summary>The item of row <paramref name="row"/>'s OPEN kebab labelled
    /// <paramref name="label"/>. Scoped to that row's own Actions cell so the item
    /// can never come from a sibling row's menu.</summary>
    private static IElement KebabItem(IRenderedComponent<GradeTopicsDialog> cut, int row, string label) =>
        KebabItems(OpenKebab(cut, row)).First(m => LabelOf(m) == label);

    /// <summary>Reads a repository source file relative to <c>src/</c> — the
    /// source-level wiring pin (the <c>StudentFormFieldsSectionEditTests</c>
    /// precedent: a Razor call site cannot be asserted from a rendered component).</summary>
    private static string ReadSource(string relative)
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..", "src", relative));
        File.Exists(srcPath).Should().BeTrue(
            $"source should exist at '{srcPath}' — check the path resolution");
        return File.ReadAllText(srcPath);
    }

    private static IReadOnlyList<IElement> Buttons(
        IRenderedComponent<GradeTopicsDialog> cut, string title) =>
        [.. cut.FindAll("fluent-button").Where(b => b.GetAttribute("title") == title)];

    /// <summary>A mocked confirm host that answers the shared destructive prompt with
    /// <paramref name="confirmed"/>. Returns the mock so the test can assert the
    /// prompt came from the SHARED <c>RowActionsMenu</c> path (it is the menu, not
    /// this dialog, that asks the question).</summary>
    private Mock<IDialogService> RegisterConfirmation(bool confirmed)
    {
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(
            Task.FromResult(confirmed ? DialogResult.Ok<object>(null!) : DialogResult.Cancel()));

        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);
        Services.AddSingleton(dialogMock.Object);
        return dialogMock;
    }

    // ── AC13 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void RendersAFluentDataGrid_WithTheOrderColumnFirstAndNoPositionText()
    {
        var cut = RenderDialog(Content());

        cut.FindAll(".fluent-data-grid").Should().HaveCount(1, "the View-all is a grid now, not custom rows");
        var headers = cut.FindAll("th").Select(h => h.TextContent.Trim()).ToArray();
        headers.Should().Equal(
            new[] { "Order", "Subject", "Strands", "Lessons", "Exceptions", "Actions" },
            "Order leads the row (the owner's revision), then the same five columns as before");

        // The Order column is not merely present — it is the row's FIRST cell.
        var orderCells = OrderCells(cut);
        orderCells.Should().HaveCount(2, "one Order cell per subject row");
        foreach (var cell in orderCells)
        {
            cell.PreviousElementSibling.Should().BeNull("the Order column is the FIRST column of the row");
        }

        // Icon-only: the 'n of N' position indicator is gone from the render.
        cut.FindAll(".topic-dialog__position").Should().BeEmpty("the order control is icon-only");
        Regex.IsMatch(cut.Find(".topic-dialog").TextContent, @"\d+ of \d+").Should().BeFalse(
            "no row renders position text any more");

        cut.FindAll("tr.fluent-data-grid-row").Where(r => r.TextContent.Contains("Mathematics"))
            .Should().HaveCount(1, "each subject is one grid row");
    }

    [TestMethod]
    public void KebabEdit_InvokesThePagesEditCallbackWithTheRow()
    {
        GradeTopicCurriculumDto? edited = null;
        var cut = RenderDialog(Content(edit: t => { edited = t; return Task.CompletedTask; }));

        var item = KebabItem(cut, row: 0, "Edit");
        item.Click();

        edited.Should().NotBeNull();
        edited!.TopicId.Should().Be(MathTopicId, "the kebab Edit reuses the page's existing edit flow");
        edited.AssignmentId.Should().Be(MathAssignmentId);
    }

    [TestMethod]
    public void KebabRemove_AsksTheSharedConfirmation_ThenInvokesThePagesRemoveCallback()
    {
        var confirm = RegisterConfirmation(confirmed: true);
        Guid? removed = null;
        var cut = RenderDialog(Content(remove: id => { removed = id; return Task.CompletedTask; }));

        KebabItem(cut, row: 0, "Remove").Click();

        confirm.Verify(d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.Is<ConfirmDialogContent>(c =>
                    c.Message.Contains("Remove subject 'Mathematics' from this grade")
                    && c.PrimaryText == "Remove"),
                It.IsAny<DialogParameters>()),
            Times.Once,
            "the destructive Remove is confirmed ONCE by the shared RowActionsMenu, not by dialog-local code");
        removed.Should().Be(MathTopicId, "the kebab Remove is destructive, so it is confirmed first");
        cut.WaitForAssertion(() =>
            cut.FindAll("tr.fluent-data-grid-row").Where(r => r.TextContent.Contains("Mathematics"))
                .Should().BeEmpty("the removed subject leaves the dialog's own list"));
    }

    [TestMethod]
    public void KebabRemove_WhenNotConfirmed_InvokesNothing()
    {
        var confirm = RegisterConfirmation(confirmed: false);
        Guid? removed = null;
        var cut = RenderDialog(Content(remove: id => { removed = id; return Task.CompletedTask; }));

        KebabItem(cut, row: 0, "Remove").Click();

        removed.Should().BeNull("a cancelled confirmation must not remove the subject");
        confirm.Verify(d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()),
            Times.Once);
        cut.WaitForAssertion(() =>
            cut.FindAll("tr.fluent-data-grid-row").Where(r => r.TextContent.Contains("Mathematics"))
                .Should().NotBeEmpty("the cancelled subject stays in the dialog's list"));
    }

    [TestMethod]
    public void ActionsCell_HoldsExactlyOneControl_TheKebab_AndNoStandaloneIconShortcuts()
    {
        var cut = RenderDialog(Content(edit: _ => Task.CompletedTask, remove: _ => Task.CompletedTask));

        var actionCells = ActionCells(cut);
        actionCells.Should().HaveCount(2, "one Actions cell per subject row");
        foreach (var cell in actionCells)
        {
            var controls = cell.QuerySelectorAll("fluent-button").ToArray();
            controls.Should().HaveCount(1, "the Actions cell holds exactly ONE control — the kebab");
            controls[0].GetAttribute("title").Should().Be(
                "Topic actions", "the single control is the shared kebab (RowActionsMenu)");
        }

        // Edit LEADS the kebab and Remove closes it (after the separator): with the
        // standalone icon shortcuts gone, the kebab is the only way in.
        KebabItems(OpenKebab(cut, row: 0)).Select(LabelOf)
            .Should().Equal("Edit", "Strands", "Teachers", "Remove");

        Buttons(cut, "Edit subject").Should().BeEmpty(
            "the owner's revision: no standalone Edit icon beside the kebab");
        Buttons(cut, "Remove subject").Should().BeEmpty(
            "the owner's revision: no standalone Delete icon beside the kebab");
    }

    // ── AC14 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ExceptionCount_FromTheExistingMap_RendersOnlyWhenPositive()
    {
        var cut = RenderDialog(Content());

        cut.Markup.Should().Contain("2 exceptions", "the count badge is fed by ExceptionCountsByTopic");
        cut.Markup.Should().NotContain("0 exceptions", "the absence of an exception renders no badge");
    }

    // ── AC15 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ReorderButtons_InvokeMoveCallbacksWithTheRowsAssignment()
    {
        GradeTopicCurriculumDto? up = null;
        GradeTopicCurriculumDto? down = null;
        var cut = RenderDialog(Content(
            moveUp: t => { up = t; return Task.CompletedTask; },
            moveDown: t => { down = t; return Task.CompletedTask; }));

        OrderCells(cut).Should().HaveCount(2, "the icon buttons sit in the leading Order column");
        cut.FindAll(".topic-dialog__position").Should().BeEmpty("the buttons are icon-only");

        Buttons(cut, "Move down")[0].Click();
        down.Should().NotBeNull();
        down!.AssignmentId.Should().Be(MathAssignmentId,
            "the dialog hands the reorder callback the row carrying its bridge assignment id");

        Buttons(cut, "Move up")[1].Click();
        up.Should().NotBeNull();
        up!.AssignmentId.Should().Be(MathAssignmentId,
            "after the local swap the moved row sits at position 2");
    }

    [TestMethod]
    public void WithoutMoveCallbacks_TheOrderColumnStaysButOffersNoButtons()
    {
        var cut = RenderDialog(Content());

        cut.FindAll("th")[0].TextContent.Trim().Should().Be("Order",
            "the column itself stays; only its controls are optional");
        cut.FindAll(".topic-dialog__position").Should().BeEmpty("the position indicator is gone for good");
        Buttons(cut, "Move up").Should().BeEmpty();
        Buttons(cut, "Move down").Should().BeEmpty();
    }

    // ── Owner revision: the page opens the Subjects dialog wider, and the dialog's
    // CONTENT ROOT — not the dialog box — is the height authority, so the Close row
    // sits on the dialog's bottom instead of being stranded mid-dialog ──

    [TestMethod]
    public void OpenTopicsDialogAsync_PassesExtraLargeAndNoHeightArgument()
    {
        var source = ReadSource(
            "Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor");

        const string signature = "private async Task OpenTopicsDialogAsync()";
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the Subjects 'View all' open method exists on the grade page");
        var end = source.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        var body = source[start..end];

        body.Should().Contain("DialogSize.ExtraLarge",
            "the six-column grid needs ExtraLarge's 960px shell (Medium is 560px)");
        body.Should().NotContain("DialogSize.Medium",
            "the Subjects grid must not fall back to the 560px Medium shell");

        // A pinned `height:` only grows the FIXED dialog box; its body is
        // `height: auto`, so the actions row stays mid-dialog. The content root owns
        // the height now (see the CSS pin below).
        var callStart = body.IndexOf("ShowReadonlyDialogAsync<GradeTopicsDialog>", StringComparison.Ordinal);
        callStart.Should().BeGreaterThan(-1, "the Subjects dialog is opened via the read-only helper");
        var callEnd = body.IndexOf(");", callStart, StringComparison.Ordinal);
        callEnd.Should().BeGreaterThan(callStart, "the open call has a closing );");
        body[callStart..callEnd].Should().NotContain("height:",
            "the open call must not pin the dialog box height — the CONTENT ROOT carries it");
    }

    // ── Owner revisions 2026-09-30: the page opens the Subjects dialog wider, and the
    // LAYOUT (height authority, one scroll region, pinned action row) is the shared
    // ReadOnlyDialogShell's — so every read-only View-all gets it, not just this one ──

    [TestMethod]
    public void GradeTopicsDialog_ComposesTheSharedShell_AndDeclaresNoLayoutOfItsOwn()
    {
        var razor = ReadSource(
            "Students/SchoolCollab.Students.Application/Components/Students/GradeTopicsDialog.razor");
        var css = ReadSource(
            "Students/SchoolCollab.Students.Application/Components/Students/GradeTopicsDialog.razor.css");

        razor.Should().Contain("<ReadOnlyDialogShell Class=\"topic-dialog\">",
            "the dialog composes the shared shell and keeps its own root class");
        razor.Should().Contain("<ChildContent>", "the grid is the shell's scroll region");
        razor.Should().Contain("<Footer>", "the Close row is the shell's pinned action row");

        // The layout belongs to the shell now — the dialog must not re-declare it
        // (ReadOnlyDialogShellTests pins the shell's own rules).
        css.Should().NotContain("height: max(72vh, 480px);", "the height belongs to the shell");
        css.Should().NotContain("max-height:", "the cap belongs to the shell");
        css.Should().NotContain("overflow-y: auto;", "the scroll region belongs to the shell");
        css.Should().NotContain("margin-top: auto;", "the pinned action row belongs to the shell");
        css.Should().NotContain("border-top:", "the footer separator belongs to the shell");

        // …and the grid still keeps its NATURAL height inside the shell's region: no
        // flex grow (that stretched its rows) and no own overflow/max-height.
        css.Should().Contain(".topic-dialog__grid { width: 100%; }",
            "the grid keeps its natural height — the shell scrolls it");
    }

    [TestMethod]
    public void Grid_OwnsNoScrollRegion_HeaderIsSticky_SoOnlyTheBodyScrolls()
    {
        var cut = RenderDialog(Content());

        // FluentUI generates the header row as the sticky one, and its own CSS pins
        // it (`position: sticky; top: 0`) to the nearest scroll region — the wrapper.
        cut.FindAll("tr[row-type='sticky-header']").Should().ContainSingle(
            "a sticky header row keeps the column titles fixed while the body scrolls");

        // The grid sits INSIDE the shell's scroll region, so the grid is not the
        // scroll context (which would stretch its rows and scroll the header away).
        var scroll = cut.FindAll(".readonly-dialog__scroll").Should().ContainSingle(
            "the shell provides the grid's one scroll region").Subject;
        scroll.QuerySelector(".fluent-data-grid").Should().NotBeNull(
            "the grid lives inside the shell's region — the shell owns the scrolling, not the grid");
    }
}
