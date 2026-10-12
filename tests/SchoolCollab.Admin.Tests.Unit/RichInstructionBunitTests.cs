using Bunit;
using FluentAssertions;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.RichText;

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
    public void Setup() => JSInterop.Mode = JSRuntimeMode.Loose;

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
