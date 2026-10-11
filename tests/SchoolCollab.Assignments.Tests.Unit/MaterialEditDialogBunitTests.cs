using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
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


    /// <summary>The seed must be VISIBLE, not merely present in the model. Owner-found (2026-10-10):
    /// editing a Text Content material appeared to LOSE its body. Asserting the submitted payload alone
    /// let a seeded-but-unpopulated field through — under a web component, a value that never reaches the
    /// rendered element is a body the author cannot see, and a submit that then pushes the empty field
    /// back into the model clears it. So this pins the RENDERED field, which is the contract the browser
    /// actually honours.</summary>
    [TestMethod]
    public async Task Edit_ShowsTheSeededBodyInTheField_NotJustInTheModel()
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

        var area = cut.Find("fluent-text-area");
        area.OuterHtml.Should().Contain("Read pages 4-6 and answer the questions.",
            "the seeded body must be in the rendered field the author reads and the browser posts back");
        cut.Find("fluent-text-field").OuterHtml.Should().Contain("Fraction basics",
            "…and so must the seeded title");
    }

    /// <summary>The write-back half — the scenario the owner hit: the author EDITS the body and saves.
    /// A seeded field that never pushes its change back leaves the body looking untouched, which is
    /// exactly "the edit lost my body". Drives the change through the rendered control, so a field whose
    /// two-way binding is not actually wired fails here rather than in the app.</summary>
    [TestMethod]
    public async Task Edit_ChangingTheBody_ReturnsTheEditedBody()
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
        cut.Find("fluent-text-area").Change("Read pages 4-6, then do the worksheet.");
        cut.Find("form").Submit();

        var result = await task;
        result.Should().NotBeNull();
        result!.Body.Should().Be("Read pages 4-6, then do the worksheet.",
            "the edited body is what the row must receive");
    }

    /// <summary>Owner requirement (2026-10-10): Enter inside a Text material's BODY must not submit the
    /// dialog — the body is multi-line prose. Pins the WIRING (the collocated module is imported and
    /// asked to guard the body field), because the behaviour itself is browser-side: FluentTextArea
    /// exposes no <c>OnKeyDown</c>, which is why the guard is a native listener rather than a Blazor
    /// handler (the <c>chatInput.js</c> precedent). A Link's dialog has no textarea, so it is not
    /// guarded at all — asserted below so the guard cannot drift into a no-op.</summary>
    [TestMethod]
    public async Task Text_BodyField_IsGuardedAgainstEnterSubmitting()
    {
        var module = JSInterop.SetupModule(MaterialEditDialog.EnterGuardModulePath);
        module.Setup<bool>("guardEnterKey", MaterialEditDialog.BodyFieldId).SetResult(true);
        var cut = RenderProvider();

        _ = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            new MaterialEditFormModel { Kind = InstructionKindDto.Text, Title = "Fraction basics" },
            "Add Text Content", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        module.Invocations.Should().Contain(
            i => i.Identifier == "guardEnterKey"
                && i.Arguments.Contains(MaterialEditDialog.BodyFieldId),
            "the body field is guarded, so Enter inserts a newline instead of submitting");
        cut.Find("fluent-text-area").Id.Should().Be(MaterialEditDialog.BodyFieldId,
            "…and the guard's selector is the field's real id");
    }

    /// <summary>The other half of the guard's scope: a Link's dialog has nothing to guard.</summary>
    [TestMethod]
    public async Task Link_HasNoBodyField_SoNothingIsGuarded()
    {
        var module = JSInterop.SetupModule(MaterialEditDialog.EnterGuardModulePath);
        var cut = RenderProvider();

        _ = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            new MaterialEditFormModel { Kind = InstructionKindDto.Url, Url = "https://khan.example/x" },
            "Add Link", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.FindAll("fluent-text-area").Should().BeEmpty("a Link has no body");
        module.Invocations.Should().NotContain(i => i.Identifier == "guardEnterKey",
            "…so there is nothing to attach the Enter guard to");
    }
}
