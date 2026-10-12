using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Components.RichText;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// AC-12 and the dialog's half of AC-14 — owed by the materials-redesign round, closed by
/// <c>round-material-row-editing</c>. The dialog MIRRORS <c>InstructionDtoValidator</c>: a Text row
/// without a title and a Link without an absolute http(s) URL are refused BEFORE the save, with the
/// reason shown, and the dialog stays open.
///
/// <para>Uses the REAL <see cref="IDialogService"/> from <c>AddFluentUIComponents</c> plus a rendered
/// <see cref="FluentDialogProvider"/> (the <c>DialogShellTests</c> harness), so the whole
/// show → validate → refuse/close path runs end to end rather than against a mock of the plumbing.</para>
/// </summary>
[TestClass]
public class MaterialEditDialogBunitTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public MaterialEditDialogBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private IRenderedComponent<FluentDialogProvider> RenderProvider() => Render<FluentDialogProvider>();

    /// <summary>AC-12 (Text half): a text material must be NAMED (D6/D7). An unnamed one is refused in
    /// the dialog with the reason — not silently sent to the API to be rejected there.</summary>
    [TestMethod]
    public async Task Text_WithoutATitle_IsRefusedWithTheReason_AndStaysOpen()
    {
        var cut = RenderProvider();
        var model = new MaterialEditFormModel
        {
            Kind = InstructionKindDto.Text,
            Title = "   ",
            Body = "The body alone is not enough.",
        };

        var dialog = await DialogService.ShowDialogAsync<MaterialEditDialog, DialogShellData<MaterialEditFormModel>>(
            new DialogShellData<MaterialEditFormModel>(model),
            DialogServiceExtensions.BuildShellParameters("Add Text Content", DialogSize.Medium));

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            cut.Find(".fluent-messagebar").TextContent.Should().Contain("A text material needs its title."));
        dialog.Result.IsCompleted.Should().BeFalse("the dialog stays open so the author can fix it");
    }

    /// <summary>AC-12 (Link half): a link's target must be absolute http(s) — a relative path is refused
    /// inside the dialog, by the same reason the API's validator would give.</summary>
    [TestMethod]
    public async Task Link_WithoutAnAbsoluteHttpUrl_IsRefusedWithTheReason_AndStaysOpen()
    {
        var cut = RenderProvider();
        var model = new MaterialEditFormModel
        {
            Kind = InstructionKindDto.Url,
            Url = "/relative/path",
        };

        var dialog = await DialogService.ShowDialogAsync<MaterialEditDialog, DialogShellData<MaterialEditFormModel>>(
            new DialogShellData<MaterialEditFormModel>(model),
            DialogServiceExtensions.BuildShellParameters("Add Link", DialogSize.Medium));

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            cut.Find(".fluent-messagebar").TextContent.Should().Contain("A link needs an absolute http(s) URL."));
        dialog.Result.IsCompleted.Should().BeFalse("the dialog stays open so the author can fix it");
    }

    /// <summary>The mirror must not OVER-refuse: an absolute https link closes with its payload, and the
    /// optional label travels with it (D7 — a Link's title is a label, never prose).</summary>
    [TestMethod]
    public async Task Link_WithAnAbsoluteUrl_ClosesWithItsPayload()
    {
        var cut = RenderProvider();
        var model = new MaterialEditFormModel
        {
            Kind = InstructionKindDto.Url,
            Url = "https://khan.example/fractions",
            Title = "Khan Academy",
        };

        var task = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            model, "Add Link", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        var result = await task;
        result.Should().NotBeNull();
        result!.Url.Should().Be("https://khan.example/fractions");
        result.Title.Should().Be("Khan Academy");
    }

    /// <summary>AC-14 (the dialog's half): editing seeds the same form — the footer says Save rather than
    /// asking the author to "Add" what already exists — and submitting returns the SEEDED values, which is
    /// what proves the seed travelled into the form (the page then writes them back onto the row).</summary>
    [TestMethod]
    public async Task Edit_IsSeededFromTheRow_AndSaysSave()
    {
        var cut = RenderProvider();
        var row = new InstructionEditorRow
        {
            Kind = InstructionKindDto.Text,
            Title = "Fraction basics",
            Text = "Read pages 4-6.",
        };

        var task = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            MaterialEditFormModel.FromRow(row), "Edit Text Content", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Markup.Should().Contain("Save", "an edit saves; it does not add")
            .And.NotContain("Add Text Content");

        cut.Find("form").Submit();

        var result = await task;
        result.Should().NotBeNull();
        result!.Title.Should().Be("Fraction basics", "the row's title seeded the form (AC-14)");
        result.Body.Should().Be("Read pages 4-6.");
    }


    /// <summary>The seed must reach the CONTROL, not merely the model (owner-found 2026-10-10, retargeted
    /// 2026-10-11): the body field is the rich editor now, so the seam is the editor's Value parameter —
    /// the seed the wrapper hands Quill through <c>LoadHTMLContent</c>. Whether it paints is browser-side
    /// (excluded from bUnit by Q17); the rendered-seed half is pinned live and by
    /// <c>RichInstructionBunitTests</c>' connected-read seam.</summary>
    [TestMethod]
    public async Task Edit_ShowsTheSeededBodyInTheEditor_NotJustInTheModel()
    {
        var cut = RenderProvider();
        var row = new InstructionEditorRow
        {
            Kind = InstructionKindDto.Text,
            Title = "Fraction basics",
            Text = "Read pages 4-6 and answer the questions.",
        };

        _ = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            MaterialEditFormModel.FromRow(row), "Edit Text Content", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.FindComponents<RichInstructionEditor>()
            .Should().ContainSingle(e => e.Instance.Id == MaterialEditDialog.BodyFieldId
                && e.Instance.Value == "Read pages 4-6 and answer the questions.",
                "the seeded body must reach the editor that paints it");
        cut.Find("fluent-text-field").OuterHtml.Should().Contain("Fraction basics",
            "…and so must the seeded title");
    }

    /// <summary>The write-back half — the scenario the owner hit: the author EDITS the body and saves.
    /// The editor has no binding (G6), so the dialog reads its HTML at submit; this pins that read
    /// travelling into the result, sanitised on the way (Q11 write-side).</summary>
    [TestMethod]
    public async Task Edit_ChangingTheBody_ReturnsTheEditedBody()
    {
        JSInterop.Setup<string>("QuillFunctions.getQuillHTML", _ => true)
            .SetResult("<p>Read pages 4-6, then do the worksheet.</p>");
        var cut = RenderProvider();
        var row = new InstructionEditorRow
        {
            Kind = InstructionKindDto.Text,
            Title = "Fraction basics",
            Text = "Read pages 4-6.",
        };

        var task = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            MaterialEditFormModel.FromRow(row), "Edit Text Content", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        var result = await task;
        result.Should().NotBeNull();
        result!.Body.Should().Be("<p>Read pages 4-6, then do the worksheet.</p>",
            "the edited body — read from the editor and sanitised — is what the row must receive");
    }

    /// <summary>Q11 write-side on the dialog's own path: whatever the editor yields is sanitised before
    /// it can become the material's body — a stored script can never ride along in the payload.</summary>
    [TestMethod]
    public async Task Text_BodyHtml_IsSanitisedOnWrite()
    {
        JSInterop.Setup<string>("QuillFunctions.getQuillHTML", _ => true)
            .SetResult("<p>Hi<script>alert(1)</script></p>");
        var cut = RenderProvider();

        var task = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            new MaterialEditFormModel { Kind = InstructionKindDto.Text, Title = "Fraction basics" },
            "Add Text Content", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        var result = await task;
        result.Should().NotBeNull();
        result!.Body.Should().Be("<p>Hi</p>", "the allow-list drops the script, never the prose");
    }

    /// <summary>An EMPTY rich editor yields Quill's <c>&lt;p&gt;&lt;br&gt;&lt;/p&gt;</c> — that is "no body",
    /// not a body worth storing (grill extension, owner 2026-10-11).</summary>
    [TestMethod]
    public async Task Text_EmptyEditorBody_StoresNull()
    {
        JSInterop.Setup<string>("QuillFunctions.getQuillHTML", _ => true).SetResult("<p><br></p>");
        var cut = RenderProvider();

        var task = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            new MaterialEditFormModel { Kind = InstructionKindDto.Text, Title = "Fraction basics" },
            "Add Text Content", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        var result = await task;
        result.Should().NotBeNull();
        result!.Body.Should().BeNull("an editor holding nothing stores no body — never the empty payload");
    }

    /// <summary>The other half of the body's scope: a Link's dialog has no body to render.</summary>
    [TestMethod]
    public async Task Link_HasNoBodyField_SoNoEditorIsRendered()
    {
        var cut = RenderProvider();

        _ = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            new MaterialEditFormModel { Kind = InstructionKindDto.Url, Url = "https://khan.example/x" },
            "Add Link", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.FindComponents<RichInstructionEditor>().Should().BeEmpty("a Link has no body");
    }
}
