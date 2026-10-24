using Bunit;
using FluentAssertions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.RichText;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// AC-3/AC-4 (round <c>wysiwyg-instruction-editor</c>): the render side. Every printing surface uses
/// this component, so its two duties are pinned here — it refuses anything the sanitiser would refuse
/// (defence in depth on display), and it leaves legacy plain text exactly as it was.
/// </summary>
[TestClass]
public class RichInstructionBunitTests : BunitContext
{
    /// <summary>The editor's interop shell is excluded from bUnit (Q17), but the component around it
    /// is not: Loose mode lets unmocked calls answer with defaults, so the render-side tests stay
    /// JS-free while the editor-seam tests can layer explicit setups on top.</summary>
    [TestInitialize]
    public void Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // The placeholder-tag menu is a FluentUI menu, so the component now needs the library's services
        // (FluentMenu resolves LibraryConfiguration) even though the editor itself never did.
        Services.AddFluentUIComponents();
    }

    [TestMethod]
    public void RendersAllowedMarkupAsRealElements()
    {
        var cut = Render<RichInstruction>(p => p
            .Add(c => c.Id, "ins")
            .Add(c => c.Value, "<p>Read <strong>pages 4-6</strong></p>"));

        cut.Find("#ins").Should().NotBeNull();
        cut.Find("#ins strong").TextContent.Should().Be("pages 4-6");
    }

    [TestMethod]
    public void NeverRendersAStoredScript()
    {
        var cut = Render<RichInstruction>(p => p
            .Add(c => c.Value, "<p>Before</p><script>alert('x')</script>"));

        cut.FindAll("script").Should().BeEmpty("display sanitises AGAIN, whatever reached storage");
        cut.Markup.Should().NotContain("alert");
        cut.Markup.Should().Contain("Before", "…and the legitimate prose still renders");
    }

    [TestMethod]
    public void PlainTextRendersExactlyAsBefore()
    {
        var cut = Render<RichInstruction>(p => p.Add(c => c.Value, "Read pages 4-6."));

        cut.Markup.Should().Contain("Read pages 4-6."); // AC-4: no migration, no reformatting
    }

    [TestMethod]
    public void EmptyOrBlankRendersNothing()
    {
        Render<RichInstruction>(p => p.Add(c => c.Value, null))
            .Markup.Should().NotContain("rich-instruction");
        Render<RichInstruction>(p => p.Add(c => c.Value, "   "))
            .Markup.Should().NotContain("rich-instruction");
    }

    /// <summary>
    /// The editor's module path must be the proven <c>./</c>-prefixed form (round
    /// <c>wysiwyg-instruction-editor</c>, the "editor not displaying" defect of 2026-10-11):
    /// blazor.web.js rewrites only <c>./</c> specifiers before <c>import()</c>, and a bare
    /// <c>_content/…</c> specifier is rejected at resolution — before any fetch — leaving the
    /// editor on its loading placeholder for ever. bUnit cannot run the interop (Q17), so this
    /// seam is pinned as a constant assertion.
    /// </summary>
    [TestMethod]
    public void EditorModulePath_UsesTheDotSlashForm_BlazorOnlyRewritesDotSlashSpecifiers()
    {
        RichInstructionEditor.AssetsModulePath.Should().StartWith("./",
            "blazor.web.js's import handler only absolutises './' specifiers; a bare one throws at resolution");
        RichInstructionEditor.AssetsModulePath.Should().StartWith("./_content/SchoolCollab.Admin.Shared/",
            "the loader is served by this library's own static web assets");
    }

    /// <summary>
    /// The seed-timing fix's interop seams (the live-instrumented 2026-10-11 defect): the editor's
    /// wrapper cannot run in bUnit (Q17), but the component around it must — when the asset import
    /// fails it stays on the loading box instead of crashing, and interop failures on the read
    /// surface as null/log rather than a page-killing exception.
    /// </summary>
    [TestMethod]
    public void Editor_WhenTheAssetLoadFails_RendersTheLoadingBox_NeverThrows()
    {
        JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath)
            .Setup<bool>("ensureQuillAssets").SetException(new JSException("offline"));

        var cut = Render<RichInstructionEditor>(p => p
            .Add(c => c.Id, "authoring-basics-instructions")
            .Add(c => c.Value, "<p>Guidance</p>"));

        cut.Find(".rich-instruction-editor").Should().NotBeNull();
        cut.Find(".rich-instruction-editor-loading").Should().NotBeNull(
            "no editor before the assets — and no crash either");
    }

    /// <summary>The unavailable-editor sentinel: null, not empty string — the caller keeps the
    /// model's current value instead of saving an empty wipe over the author's prose.</summary>
    [TestMethod]
    public async Task GetHtmlAsync_WithoutAMountedEditor_ReturnsNull_NeverThrows()
    {
        JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath)
            .Setup<bool>("ensureQuillAssets").SetException(new JSException("offline"));
        var cut = Render<RichInstructionEditor>(p => p.Add(c => c.Value, "<p>Guidance</p>"));

        (await cut.Instance.GetHtmlAsync()).Should().BeNull(
            "an unavailable editor must not wipe the model or fault the page");
    }

    /// <summary>The happy seam: assets mocked, the wrapper's interop answering — the read goes
    /// through unchanged.</summary>
    [TestMethod]
    public async Task GetHtmlAsync_WithAConnectedEditor_ReturnsTheWrapperHtml()
    {
        JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);
        JSInterop.Setup<string>("QuillFunctions.getQuillHTML", _ => true).SetResult("<p>Read pages 4-6</p>");

        var cut = Render<RichInstructionEditor>(p => p.Add(c => c.Value, "<p>Read pages 4-6</p>"));

        (await cut.Instance.GetHtmlAsync()).Should().Be("<p>Read pages 4-6</p>");
    }

    // ── Placeholder-tag menu (template-editor round, B3) ─────────────────────────────────────────────
    // bUnit cannot insert into Quill (there is no Quill), so what is pinned here is the CONTRACT across
    // the boundary: which tags the menu offers and in what order, that a tag-less host gets nothing new,
    // that the menu and its button live INSIDE the editor's toolbar, and that a menu click reaches the
    // module's insert function with the token and the root id — never the wrapper's non-unique
    // QuillEditor/QuillToolBar ids.
    // NOT pinned, because bUnit cannot see it: where the popover actually lands. It is anchored to the
    // toolbar button by the library's own CSS anchor positioning (FluentMenu.Trigger); that the anchor is
    // inside the editor box is asserted structurally here, and the rest is a live-browser fact.
    // The toolbar is addressed by the wrapper's own fixed id (#QuillToolBar): Quill adds the ql-toolbar
    // class at runtime, and bUnit never runs Quill.

    /// <summary>The tags a host passes render as the grouped menu — one header, immediately followed by
    /// its own tokens, in the order the group map declares.</summary>
    [TestMethod]
    public void PlaceholderTags_RenderEachGroupHeaderImmediatelyAboveItsOwnTokens()
    {
        // Arrange
        JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);

        // Act
        var cut = Render<RichInstructionEditor>(p => p
            .Add(c => c.Id, "probe-editor")
            .Add(c => c.PlaceholderTags, NotificationTemplateTags.All));

        // Assert — the whole sequence, so a header that drifted below its tokens (or a group that lost
        // one) fails here: a containment-only assertion passes whatever the order.
        cut.FindAll("fluent-menu-item").Select(i => i.OwnLabel()).Should().Equal(
        [
            "Assignment", "{{Title}}", "{{Description}}", "{{DueDate}}",
            "Content", "{{Instructions}}",
            "Recipients", "{{StudentName}}", "{{GuardianName}}", "{{GuardianRole}}",
            "School & links", "{{SchoolName}}", "{{Link}}", "{{LinkExpiry}}",
        ]);
    }

    /// <summary>
    /// The one affordance: a button in the Quill toolbar (the editor's own box), with a stable id the
    /// menu anchors to — not a page-level trigger beside the editor, and not a right-click menu.
    /// </summary>
    [TestMethod]
    public void WithPlaceholderTags_TheTagButtonAndItsMenuSitInsideTheEditorToolbar()
    {
        // Arrange
        JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);

        // Act
        var cut = Render<RichInstructionEditor>(p => p
            .Add(c => c.Id, "template-body-editor")
            .Add(c => c.PlaceholderTags, NotificationTemplateTags.All));

        // Assert
        var toolbar = cut.Find("#QuillToolBar");
        var button = toolbar.QuerySelector("button[aria-label='Insert tag']");
        button.Should().NotBeNull("the tag affordance lives in the editor's toolbar");
        button!.GetAttribute("id").Should().Be("template-body-editor-insert-tag",
            "the id is the one the menu anchors to, and it is the editor root's — unique per instance");
        button.GetAttribute("type").Should().Be("button",
            "a toolbar button must never submit a surrounding form");
        button.GetAttribute("aria-haspopup").Should().Be("menu");
        button.GetAttribute("aria-expanded").Should().Be("false", "the menu starts closed");
        button.TextContent.Trim().Should().Be("Insert tag",
            "the button is labelled in words — the two-brace glyph was unreadable at toolbar size");

        toolbar.QuerySelector("fluent-menu").Should().NotBeNull("its item list is rendered with it...");
        cut.FindAll("fluent-menu").Should().ContainSingle("...and nowhere else on the component");
    }

    /// <summary>A tag the group map does not name is still reachable, under the trailing "Other" group.</summary>
    [TestMethod]
    public void ATagMissingFromTheGroupMap_FallsIntoTheTrailingOtherGroup()
    {
        // Arrange
        JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);

        // Act
        var cut = Render<RichInstructionEditor>(p => p
            .Add(c => c.Id, "probe-editor")
            .Add(c => c.PlaceholderTags, ["Title", "NotAGroupedTag"]));

        // Assert
        var items = cut.FindAll("fluent-menu-item").Select(i => i.OwnLabel()).ToList();
        items.Should().Contain("Other", "no vocabulary entry may become unreachable");
        items.Should().Contain("{{NotAGroupedTag}}");
        items.IndexOf("Other").Should().BeLessThan(items.IndexOf("{{NotAGroupedTag}}"),
            "the trailing group's own header sits above the tokens it holds");
    }

    /// <summary>
    /// The pre-existing host contract: with no tags there is no button, no menu and no interop beyond
    /// the asset load — the assignment authoring surface renders exactly as it always has.
    /// </summary>
    [TestMethod]
    public void WithoutPlaceholderTags_ThereIsNoTagButtonAndNoMenu()
    {
        // Arrange
        var module = JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);

        // Act
        var cut = Render<RichInstructionEditor>(p => p.Add(c => c.Id, "authoring-basics-instructions"));

        // Assert
        cut.FindAll("fluent-menu").Should().BeEmpty();
        cut.FindAll("button[aria-label='Insert tag']").Should().BeEmpty(
            "a host that offers no tags gets no affordance either");
        module.Invocations.Should().OnlyContain(i => i.Identifier == "ensureQuillAssets",
            "the editor's only interop here is the asset load");
    }

    /// <summary>
    /// Clicking a tag inserts through the module's selection function, with the token as the item's own
    /// label. The real caret arithmetic lives in JS (it cannot run here); this pins that the click
    /// reaches it, once, with the right arguments.
    /// </summary>
    [TestMethod]
    public void ClickingATag_InsertsItThroughTheSelectionFunction()
    {
        // Arrange
        var module = JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);
        var cut = Render<RichInstructionEditor>(p => p
            .Add(c => c.Id, "template-body-editor")
            .Add(c => c.PlaceholderTags, NotificationTemplateTags.All));

        // Act
        cut.FindAll("fluent-menu-item")
            .Single(i => i.TextContent.Trim() == "{{Title}}")
            .MenuItemClick("{{Title}}");

        // Assert
        module.Invocations.Where(i => i.Identifier == "insertPlaceholderAtSelection")
            .Should().ContainSingle("one click inserts one token — the menu's own bubbling must not double it")
            .Which.Arguments.Should().Equal("template-body-editor", "{{Title}}");
    }

    /// <summary>
    /// A group parent's label never becomes a token. <c>FluentMenu.OnClick</c> is MENU-scoped, so
    /// selecting a group parent reaches the handler too — and its reported text is prose ("Assignment"),
    /// or the whole nested sub-menu's text, either of which would land in a parent's message body.
    /// </summary>
    [TestMethod]
    public void ClickingAGroupHeader_InsertsNothing()
    {
        // Arrange
        var module = JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);
        var cut = Render<RichInstructionEditor>(p => p
            .Add(c => c.Id, "template-body-editor")
            .Add(c => c.PlaceholderTags, NotificationTemplateTags.All));
        var groupHeader = cut.FindAll("fluent-menu-item").Single(i => i.OwnLabel() == "Assignment");

        // Act — the group label, then the shape a parent carrying its sub-menu reports.
        groupHeader.MenuItemClick("Assignment");
        groupHeader.MenuItemClick("Assignment{{Title}}{{Description}}{{DueDate}}");

        // Assert
        module.Invocations.Should().NotContain(i => i.Identifier == "insertPlaceholderAtSelection",
            "only the strict {{Name}} token shape may reach the editor — never a group's label");
    }

    /// <summary>A read-only editor is not an insertion target, so it gets neither the button nor the menu.</summary>
    [TestMethod]
    public void ReadOnly_WithPlaceholderTags_HasNoTagButtonAndNoMenu()
    {
        // Arrange
        JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);

        // Act
        var cut = Render<RichInstructionEditor>(p => p
            .Add(c => c.Id, "template-body-editor")
            .Add(c => c.ReadOnly, true)
            .Add(c => c.PlaceholderTags, NotificationTemplateTags.All));

        // Assert
        cut.FindAll("fluent-menu").Should().BeEmpty();
        cut.FindAll("button[aria-label='Insert tag']").Should().BeEmpty();
    }

    /// <summary>
    /// The gate is re-evaluated every render, so the affordance must follow it: a host that gains tags
    /// after the first render gets the button and its menu.
    /// </summary>
    [TestMethod]
    public void GainingTagsAfterTheFirstRender_RendersTheTagButton()
    {
        // Arrange
        JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);
        var cut = Render<RichInstructionEditor>(p => p.Add(c => c.Id, "template-body-editor"));
        cut.FindAll("button[aria-label='Insert tag']").Should().BeEmpty();

        // Act
        cut.Render(p => p
            .Add(c => c.Id, "template-body-editor")
            .Add(c => c.PlaceholderTags, NotificationTemplateTags.All));

        // Assert
        cut.Find("#QuillToolBar button[aria-label='Insert tag']").Should().NotBeNull();
        cut.FindAll("fluent-menu").Should().ContainSingle();
    }

    /// <summary>The other direction: read-only removes the button, and the menu goes with it.</summary>
    [TestMethod]
    public void TurningReadOnlyOn_RemovesTheTagButtonItsMarkupNoLongerNeeds()
    {
        // Arrange
        JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);
        var cut = Render<RichInstructionEditor>(p => p
            .Add(c => c.Id, "template-body-editor")
            .Add(c => c.PlaceholderTags, NotificationTemplateTags.All));

        // Act
        cut.Render(p => p
            .Add(c => c.Id, "template-body-editor")
            .Add(c => c.ReadOnly, true)
            .Add(c => c.PlaceholderTags, NotificationTemplateTags.All));

        // Assert
        cut.FindAll("button[aria-label='Insert tag']").Should().BeEmpty();
        cut.FindAll("fluent-menu").Should().BeEmpty();
    }

    /// <summary>The failure seam: a JS failure on the read returns null and is logged — it must
    /// never propagate into SaveAsync and take the page's ErrorBoundary down (the defect's sibling
    /// on the save path).</summary>
    [TestMethod]
    public async Task GetHtmlAsync_WhenTheInteropThrows_ReturnsNull_NeverPropagates()
    {
        JSInterop.SetupModule(RichInstructionEditor.AssetsModulePath);
        JSInterop.Setup<string>("QuillFunctions.getQuillHTML", _ => true)
            .SetException(new JSException("editor not initialised"));

        var cut = Render<RichInstructionEditor>(p => p.Add(c => c.Value, "<p>Read pages 4-6</p>"));

        (await cut.Instance.GetHtmlAsync()).Should().BeNull();
    }
}
