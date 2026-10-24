using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Components.RichText;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The shared editor's surface (naming spec D1/D9/D12), asserted through the rendered page rather than
/// through its stylesheet: the naming rows sit above one "Template Type" row that holds BOTH the kind
/// and the contact-mode dropdown, the action bar repeats neither the kind nor the page's address, and
/// "View all constraints" waits until the chip row stops being scannable.
/// </summary>
[TestClass]
public class TemplateEditorSurfaceTests : BunitContext
{
    public TemplateEditorSurfaceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private TemplateApiHandler Register() => TemplateTestServices.Register<TemplateEditor>(this);

    private IRenderedComponent<TemplateEditor> RenderCreate() =>
        Render<TemplateEditor>(p => p.Add(x => x.Mode, TemplateEditorMode.Create));

    private IRenderedComponent<TemplateEditor> RenderEdit(Guid id) =>
        Render<TemplateEditor>(p => p
            .Add(x => x.Mode, TemplateEditorMode.Edit)
            .Add(x => x.Id, id));

    /// <summary>The FormRow whose label is <paramref name="label"/> — the row is the unit under test.</summary>
    private static IRenderedComponent<FormRow> RowLabelled(
        IRenderedComponent<TemplateEditor> cut, string label) =>
        cut.FindComponents<FormRow>().Single(r => r.Instance.Label == label);

    /// <summary>The <c>.form-row</c> a control sits in.</summary>
    private static IElement RowOf(IElement control) =>
        control.Ancestors().OfType<IElement>().First(a => a.ClassList.Contains("form-row"));

    [TestMethod]
    public void TheTemplateTypeRow_HoldsBothDropdownsInTheirOwnCells_UnderOneGroupLabel()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-title").Should().ContainSingle());

        // Assert
        var dropdowns = cut.FindAll("[aria-label='Template type'], [aria-label='Preferred mode of contact']");
        dropdowns.Should().HaveCount(2, "the kind and the contact mode are both rendered");

        var rows = dropdowns.Select(RowOf).Distinct().ToList();
        rows.Should().ContainSingle(
            "type and preferred mode of contact are ONE decision, so they share one row (D9)");
        rows[0].QuerySelector(".form-row-label")!.TextContent.TrimStart()
            .Should().StartWith("Template Type", "…under the one label that names the decision");

        // The shared cell pattern (the AssignmentAuthoring "Grading" row): each picker is named by its own
        // caption under the group label, and the row keeps the label-left placement that lines the two
        // pickers up with every other row's inputs.
        var typeRow = RowLabelled(cut, "Template Type");
        typeRow.Instance.AlignTop.Should().BeTrue("a two-input row top-aligns, per the shared row primitive");
        typeRow.Instance.LabelPosition.Should().Be(RowLabelPosition.Default,
            "LabelPosition=Below would drop the 180px label gutter the whole form aligns on");

        var cells = cut.FindComponents<FormRowCell>();
        cells.Select(c => c.Instance.Caption).Should().Equal(
            new[] { "Type", "Preferred mode of contact" },
            "each cell names the input it holds");
        cells.Should().OnlyContain(c => c.Instance.Help != null, "the cell captions carry the hints");
        // A caption only LABELS something when its <label for> resolves: FormRowCell renders
        // <label for="@For">, so each cell's For must be the id its own control carries.
        cells.Select(c => c.Instance.For).Should().Equal(
            new[] { "template-kind", "template-channel" },
            "a caption with no For labels nothing and clicking it does nothing");
        cut.FindAll("label[for='template-kind']")
            .Should().Contain(l => l.TextContent.TrimStart().StartsWith("Type", StringComparison.Ordinal),
                "the first cell's caption is a <label> that really points at the kind dropdown");
        cut.Find("#template-kind").Should().NotBeNull("…which carries the id the caption names");
        cut.FindAll("label[for='template-channel']")
            .Should().Contain(l => l.TextContent.TrimStart().StartsWith("Preferred mode of contact", StringComparison.Ordinal));
        cut.Find("#template-channel").Should().NotBeNull("…as does the channel dropdown");
        // Containment asserted from the ROW (not by walking a cell's ancestors): the two cells must be
        // the two children of the one row the two dropdowns share.
        rows[0].QuerySelectorAll(".form-row-cell").Should().HaveCount(2,
            "both cells live in the one row the two dropdowns share");
    }

    /// <summary>
    /// Every panel on the editor is TITLED, Message included. The heading was removed for a few hours
    /// on 2026-10-23 (the fields label themselves) and the owner restored it the same day, so this
    /// pins the restored contract: three titled panels, each with its rule.
    /// </summary>
    [TestMethod]
    public void EveryPanelOnTheEditor_CarriesItsHeading()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-title").Should().ContainSingle());

        // Assert — the Message panel is titled and ruled like its neighbours.
        var message = cut.Find("#template-editor-message");
        message.ClassList.Should().Contain("panel-section", "the card chrome is the shared component's");
        cut.Find("#template-editor-message-title").TextContent.Trim().Should().Be("Message");
        message.QuerySelectorAll(".panel-section__rule").Should().ContainSingle(
            "the rule separates a header from its content, so it comes with the heading");
        message.QuerySelector("#template-title").Should().NotBeNull("the fields stay inside the panel");

        cut.Find("#template-constraints-title").TextContent.Trim().Should().Be("Constraints");
        cut.Find("#template-preview-title").TextContent.Trim().Should().Be("Preview");
    }

    /// <summary>
    /// The owner-reported defect (2026-10-23): with <c>FEATURE:EnableActivityGroups</c> off,
    /// <c>/activity-groups</c> is not mapped, so it answers 404 — and the editor used to answer that
    /// with an "constraint names are unavailable" banner, an empty Constraints panel and a wholly
    /// disabled add menu. An absent optional feature must cost exactly its own dimension, and must not
    /// be reported as an outage.
    /// </summary>
    [TestMethod]
    public void WhenTheActivityGroupRouteIsNotMapped_OnlyThatDimensionIsLost_AndNoWarningIsShown()
    {
        // Arrange — the flag-off posture: the route does not exist (404, not 500).
        var handler = Register();
        handler.UnmappedPaths.Add("/activity-groups");

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-title").Should().ContainSingle());

        // Assert — no outage banner: a feature this school does not have is not a fault.
        cut.FindAll("#template-lookup-warning").Should().BeEmpty(
            "a flag-off route is a posture, not an outage — the old code reported it as one");

        // …and the menu offers every dimension except the one the host does not serve.
        var labels = cut.FindComponents<RowActionsMenu>().Single().Instance.Actions
            .Select(action => action.Label).ToArray();
        labels.Should().BeEquivalentTo(
            ["Add grade levels", "Add streams", "Add subjects", "Add assignment types"],
            "the activity-group dimension is hidden rather than offered empty");
        cut.FindComponents<RowActionsMenu>().Single().Instance.Actions
            .Should().OnlyContain(action => action.Disabled == false,
                "every offered dimension loaded, so none is disabled");
    }

    /// <summary>A genuine lookup outage still reports itself, and loses only its own dimension.</summary>
    [TestMethod]
    public void WhenTheLookupsFail_TheWarningIsShown_AndEveryDimensionIsStillOfferedDisabled()
    {
        // Arrange
        var handler = Register();
        handler.LookupsFail = true;

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-title").Should().ContainSingle());

        // Assert
        cut.Find("#template-lookup-warning").TextContent.Should().Contain("Some constraint names are unavailable");

        var actions = cut.FindComponents<RowActionsMenu>().Single().Instance.Actions;
        actions.Should().HaveCount(5, "an outage keeps every dimension visible");
        actions.Where(action => action.Label != "Add assignment types").Should().OnlyContain(action => action.Disabled,
            "every looked-up dimension is unfillable while the lookups are down");
        actions.Single(action => action.Label == "Add assignment types").Disabled.Should().BeFalse(
            "assignment types are a closed set — they need no lookup, so an outage cannot reach them");
    }

    /// <summary>
    /// A5/fallback: the chip row AND the page-level "Insert tag" trigger are both gone. The one affordance
    /// left is a button in the editor's own Quill toolbar, inside the editor box — so the menu it opens
    /// cannot land anywhere but on the text it inserts into.
    /// </summary>
    [TestMethod]
    public void ThePageHasNoInsertTagTrigger_TheOneAffordanceIsInTheEditorToolbar()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-title").Should().ContainSingle());

        // Assert — the chip row and the panel-level trigger no longer exist.
        cut.FindAll("#template-tag-chips").Should().BeEmpty();
        cut.FindAll("fluent-button")
            .Where(b => b.GetAttribute("title")?.StartsWith("Insert {", StringComparison.Ordinal) == true)
            .Should().BeEmpty("the chips were per-tag buttons titled 'Insert {{Tag}}'");
        cut.FindAll("fluent-button").Where(b => b.TextContent.Trim() == "Insert tag").Should().BeEmpty(
            "the page-level trigger was removed — the editor's toolbar is the one affordance");

        // #QuillToolBar is the editor wrapper's own toolbar container id; Quill adds the ql-toolbar class
        // at runtime, and bUnit never runs Quill.
        cut.WaitForAssertion(() => cut.FindAll("#QuillToolBar button[aria-label='Insert tag']")
            .Should().ContainSingle("the tag affordance sits in the editor's own toolbar"));
    }

    /// <summary>The toolbar button's menu is the grouped placeholder menu, rendered inside the editor box.</summary>
    [TestMethod]
    public void TheToolbarTagMenu_RendersTheFourPlaceholderGroups()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#QuillToolBar fluent-menu").Should().ContainSingle());

        // Assert
        var labels = cut.Find("#QuillToolBar fluent-menu")
            .QuerySelectorAll("fluent-menu-item")
            .Select(i => i.OwnLabel())
            .ToList();
        labels.Should().Contain(["Assignment", "Content", "Recipients", "School & links"],
            "the placeholder vocabulary is grouped, and the groups are collapsed sub-menus");
        labels.Should().Contain("{{Title}}", "the item's label is the literal token that gets inserted");
        labels.Should().Contain("{{LinkExpiry}}", "the last tag of the last group is reachable");
    }

    /// <summary>
    /// A5: a menu item inserts through the module's selection call, addressed to the editor root the page
    /// pins — never to the wrapper's repeated ids.
    /// </summary>
    [TestMethod]
    public void TheToolbarTagMenu_InsertsThroughTheEditorsSelectionFunction()
    {
        // Arrange
        Register();
        var module = JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);

        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#QuillToolBar fluent-menu-item").Should().NotBeEmpty());

        // Act
        cut.Find("#QuillToolBar fluent-menu")
            .QuerySelectorAll("fluent-menu-item")
            .Single(i => i.OwnLabel() == "{{StudentName}}")
            .MenuItemClick("{{StudentName}}");

        // Assert
        module.Invocations.Where(i => i.Identifier == "insertPlaceholderAtSelection")
            .Should().ContainSingle()
            .Which.Arguments.Should().Equal(
                new object[] { "template-body-editor", "{{StudentName}}" },
                "the token lands at the caret, in the editor the page pinned");
    }

    /// <summary>
    /// A read-only row is not an insertion target: the editor behind it renders no tag button and no menu,
    /// and the page adds none of its own.
    /// </summary>
    [TestMethod]
    public void OnAReadOnlyRow_ThereIsNoTagButtonAndNoMenu()
    {
        // Arrange
        var handler = Register();

        // A system-blueprint row opened in a tenant session (redesign spec §3.7) is the read-only case.
        var blueprint = new TemplateRow
        {
            Kind = 0, Channel = 1, Title = "System reminder", Body = "BODY", TenantId = null,
        };
        handler.Rows.Add(blueprint);

        // Act
        var cut = RenderEdit(blueprint.Id);

        // Assert
        cut.WaitForAssertion(() => cut.FindAll("#template-adopt").Count.Should().Be(1,
            "the read-only row's action is the copy action"));
        cut.FindAll("button[aria-label='Insert tag']").Should().BeEmpty();
        cut.FindAll("fluent-menu").Should().BeEmpty();
    }

    /// <summary>
    /// The message panel's ratified widths (owner report 2026-10-23): the title FILLS the row instead of
    /// taking a fixed step (at w-8 it shrinks with its flex track and ends up the same width as the code),
    /// the subject matches the title, the code keeps its 200px step, and both pickers take the 240px one.
    /// </summary>
    [TestMethod]
    public void TheMessageFields_TakeTheirRatifiedWidths()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-subject").Should().ContainSingle());

        // Assert
        string InputWidth(string id) => cut.FindComponents<FluentTextInput>()
            .Single(i => i.Instance.Id == id).Instance.Class;

        InputWidth("template-title").Should().Be("w-7",
            "the title takes the 320px step (W7) — wide, but not full width");
        InputWidth("template-subject").Should().Be("w-7", "the subject is as wide as the title");
        InputWidth("template-code").Should().Be("w-4", "the code keeps its 200px step, and it never changes");
        cut.FindComponents<FluentTextArea>().Single(i => i.Instance.Id == "template-description")
            .Instance.Class.Should().Be("w-9", "the description stays full width");

        RowLabelled(cut, "Template Type")
            .FindAll("[style*='width:240px']")
            .Should().HaveCount(2, "both pickers take the ladder's 240px step (W5)");
    }

    [TestMethod]
    public void TheNamingRows_SitAboveTheTemplateTypeRow_WithARequiredTitle()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-title").Should().ContainSingle());

        // Assert
        var labels = cut.FindComponents<FormRow>().Select(r => r.Instance.Label).ToList();

        labels.IndexOf("Title").Should().BeGreaterThanOrEqualTo(0, "a template is named on create (D1)");
        labels.IndexOf("Title").Should().BeLessThan(labels.IndexOf("Template Type"));
        labels.IndexOf("Description").Should().BeGreaterThan(labels.IndexOf("Title"));
        labels.IndexOf("Description").Should().BeLessThan(labels.IndexOf("Template Type"));

        RowLabelled(cut, "Title").Instance.Required.Should().BeTrue(
            "no template may be created nameless (D1/D2)");
        RowLabelled(cut, "Description").Instance.Required.Should().BeFalse(
            "the description is optional metadata (D1)");
    }

    [TestMethod]
    public void TheCodeRow_SitsUnderTheTitle_IsReadOnly_AndADraftShowsADash()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-code").Should().ContainSingle());

        // Assert — D6/D7 and stories 10-12: the code is shown, never typed, and a draft that has not
        // been saved has not been minted yet.
        var code = cut.FindComponents<FluentTextInput>().Single(i => i.Instance.Id == "template-code");
        code.Instance.Value.Should().Be("—", "a draft has no code yet, and an empty box would read as a required input");
        code.Instance.Disabled.Should().BeTrue("the code is generated, never typed (D6)");

        cut.Find("#template-code-hint").TextContent.Should().Contain("Generated when the template is saved",
            "the operator is told where the empty field comes from (story 12)");

        var labels = cut.FindComponents<FormRow>().Select(r => r.Instance.Label).ToList();
        labels.IndexOf("Code").Should().BeGreaterThan(labels.IndexOf("Title"),
            "the code belongs beside the name it belongs to");
        labels.IndexOf("Code").Should().BeLessThan(labels.IndexOf("Template Type"),
            "…and above the decision row, not inside the message body's flow");
    }

    [TestMethod]
    public void TheCodeRow_OnASavedRow_ShowsTheMintedCode()
    {
        // Arrange
        var handler = Register();
        var row = new TemplateRow
        {
            Kind = 0, Channel = 1, Title = "Grade 8 Science reminder", Body = "BODY", Code = "TPL07",
        };
        handler.Rows.Add(row);

        // Act
        var cut = RenderEdit(row.Id);

        // Assert
        cut.WaitForAssertion(() =>
        {
            var code = cut.FindComponents<FluentTextInput>().Single(i => i.Instance.Id == "template-code");
            code.Instance.Value.Should().Be("TPL07", "the minted reference is shown, so support can quote it");
            code.Instance.Disabled.Should().BeTrue("and it is never editable, even on an editable row");
            cut.Find("#template-code-hint").TextContent.Should().Contain("Generated on save");
        });
    }

    /// <summary>
    /// The page header repeats neither the kind nor the page's address — and, since 2026-10-23, it is
    /// not an ACTION BAR at all: the page action, Reset and Cancel render in the Message panel's sticky
    /// header row (panel-layout-pattern §4/§6), so the shell contributes the title block and the
    /// breadcrumb only.
    /// </summary>
    [TestMethod]
    public void ThePageHeader_RepeatsNeitherTheKindNorThePagesAddress_AndCarriesNoActions()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-save").Should().ContainSingle());

        // Assert — no separate action bar on this surface: the actions live in the first block's header.
        cut.FindAll(".authoring-actionbar").Should().BeEmpty(
            "the page action belongs to the Message panel's sticky header row (§4/§6)");
        cut.Find("#template-editor-message .panel-section__actions #template-save").Should().NotBeNull(
            "…which is where it now renders, inside the FIRST block");

        var titleBlock = cut.Find(".authoring-page-title");

        titleBlock.QuerySelectorAll(".kind-banner").Should().BeEmpty(
            "the kind is chosen a few centimetres below the header, so a banner there says it twice (D9)");
        titleBlock.TextContent.Should().NotContain("New template",
            "the breadcrumb is the one place that says where the operator is (D9)");
        cut.Find("#authoring-breadcrumb").TextContent.Should().Contain("New template",
            "…and it still says it");
    }

    [TestMethod]
    public void OnEdit_TheActionBarHeading_IsTheTemplatesTitle_AndTheOwnerStaysBesideIt()
    {
        // Arrange
        var handler = Register();
        var row = new TemplateRow
        {
            Kind = 0, Channel = 1, Title = "Grade 8 Science reminder", Body = "BODY",
        };
        handler.Rows.Add(row);

        // Act
        var cut = RenderEdit(row.Id);

        // Assert
        cut.WaitForAssertion(() =>
        {
            cut.Find(".authoring-heading").TextContent.Should().Contain("Grade 8 Science reminder",
                "the page names itself by the row it has open (D3/D9)");
            cut.FindAll(".authoring-actionbar .kind-banner").Should().BeEmpty("still no kind banner");
            cut.Find("#template-editor-owner").Should().NotBeNull(
                "the owner chip stays on edit, where it says whose row this is (D9)");
        });
    }

    [TestMethod]
    public void ViewAllConstraints_AppearsOnlyOnceTheChipRowStopsBeingScannable()
    {
        // Arrange
        var handler = Register();
        var atTheThreshold = new TemplateRow
        {
            Kind = 0,
            Channel = 1,
            Title = "Five constraints",
            Body = "BODY",
            Constraints = [.. Enumerable.Range(1, 5).Select(i => (1, $"stream-{i}"))],
        };
        var aboveTheThreshold = new TemplateRow
        {
            Kind = 0,
            Channel = 1,
            Title = "Six constraints",
            Body = "BODY",
            Constraints = [.. Enumerable.Range(1, 6).Select(i => (1, $"stream-{i}"))],
        };
        handler.Rows.AddRange([atTheThreshold, aboveTheThreshold]);

        // Act
        var five = RenderEdit(atTheThreshold.Id);
        five.WaitForAssertion(() => five.FindAll("#template-constraints-chips").Should().ContainSingle());

        // Assert
        five.FindAll("#template-constraints-view-all").Should().BeEmpty(
            "five constraints still fit the chip row, so the link would only duplicate it (D12)");

        var six = RenderEdit(aboveTheThreshold.Id);
        six.WaitForAssertion(() => six.FindAll("#template-constraints-chips").Should().ContainSingle());
        six.Find("#template-constraints-view-all").TextContent.Should().Contain("View all constraints",
            "past five constraints the full list earns its one link (D12)");
    }
}
