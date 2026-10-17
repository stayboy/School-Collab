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

    /// <summary>Builds the v5 options for opening <see cref="MaterialEditDialog"/> directly: the shell
    /// options plus the <c>Content</c> entry FluentUI 5.x spreads onto the dialog's typed
    /// <c>[Parameter]</c>. FluentUI 5.x completes the show task only when the dialog CLOSES, so
    /// callers hold the task to observe an open dialog.</summary>
    private static DialogOptions MaterialOptions(MaterialEditFormModel model, string title)
    {
        var options = DialogServiceExtensions.BuildShellOptions(title, DialogWidth.Medium);
        options.Parameters[nameof(DialogShellBase<MaterialEditFormModel, MaterialEditResult>.Content)] =
            new DialogShellData<MaterialEditFormModel>(model);
        return options;
    }

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

        var task = DialogService.ShowDialogAsync(typeof(MaterialEditDialog), MaterialOptions(model, "Add Text Content"));

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            cut.Find("fluent-message-bar").TextContent.Should().Contain("A text material needs its title."));
        task.IsCompleted.Should().BeFalse("the dialog stays open so the author can fix it");
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

        var task = DialogService.ShowDialogAsync(typeof(MaterialEditDialog), MaterialOptions(model, "Add Link"));

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            cut.Find("fluent-message-bar").TextContent.Should().Contain("A link needs an absolute http(s) URL."));
        task.IsCompleted.Should().BeFalse("the dialog stays open so the author can fix it");
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
            model, "Add Link", DialogWidth.Medium);

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
            MaterialEditFormModel.FromRow(row), "Edit Text Content", DialogWidth.Medium);

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
            MaterialEditFormModel.FromRow(row), "Edit Text Content", DialogWidth.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.FindComponents<RichInstructionEditor>()
            .Should().ContainSingle(e => e.Instance.Id == MaterialEditDialog.BodyFieldId
                && e.Instance.Value == "Read pages 4-6 and answer the questions.",
                "the seeded body must reach the editor that paints it");
        cut.FindComponents<FluentTextInput>()
            .Should().ContainSingle(e => e.Instance.Value == "Fraction basics",
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
            MaterialEditFormModel.FromRow(row), "Edit Text Content", DialogWidth.Medium);

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
            "Add Text Content", DialogWidth.Medium);

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
            "Add Text Content", DialogWidth.Medium);

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
            "Add Link", DialogWidth.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.FindComponents<RichInstructionEditor>().Should().BeEmpty("a Link has no body");
    }

    /// <summary>Round media-pill-listing (C2/Q10, Slice F): the dialog accepts a MEDIA row — it renders
    /// the optional Title + Description pair (never a rich body, never the file fields), and BOTH being
    /// blank PASSES validation (the fields are independent and optional). A filled pair returns in the
    /// <see cref="MaterialEditResult"/> for the row's host to apply. Pre-fix: a media kind fell into the
    /// Link branch and never seeded/yielded media metadata.</summary>
    [TestMethod]
    public async Task MediaKindDialog_AcceptsAMediaRow_OptionalTitleAndBody_SubmitsThem()
    {
        var row = new InstructionEditorRow
        {
            Id = Guid.NewGuid(),
            Kind = InstructionKindDto.Audio,
            FileName = "clip.mp3",
            ContentType = "audio/mpeg",
            FileSize = 2048,
            StoragePath = "tenants/t/staging/g/clip.mp3",
        };

        // ── Both blank: independent and optional, so nothing refuses it. ──
        var blankCut = RenderProvider();
        var blankTask = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            MaterialEditFormModel.FromRow(row), "Edit Material", DialogWidth.Medium);

        blankCut.WaitForAssertion(() => blankCut.Find("form").Should().NotBeNull());
        blankCut.FindAll("form fluent-text-input").Should().HaveCount(1,
            "a media row renders its optional Title field; the Description is the shared rich editor");
        blankCut.Markup.Should().Contain("Title (optional)").And.Contain("Description (optional)");
        blankCut.FindComponents<RichInstructionEditor>().Should().ContainSingle(
            e => e.Instance.Id == MaterialEditDialog.BodyFieldId,
            "the media Description IS the same rich editor a Text material's body uses (Q10's letter)");
        blankCut.Find("form").Submit();

        var blankResult = await blankTask;
        blankResult.Should().NotBeNull("both fields are optional — nothing to fix, nothing refused");
        blankResult!.Kind.Should().Be(InstructionKindDto.Audio);
        blankResult.Title.Should().BeNull();
        blankResult.Body.Should().BeNull();

        // ── A filled pair returns in the result the host writes back onto the row. ──
        var filledCut = RenderProvider();
        var filledTask = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            MaterialEditFormModel.FromRow(new InstructionEditorRow
            {
                Kind = InstructionKindDto.Image,
                FileName = "diagram.png",
                ContentType = "image/png",
                FileSize = 2048,
                StoragePath = "tenants/t/staging/g/diagram.png",
                Title = "Diagram",
                Text = "Label the parts.",
            }),
            "Edit Material", DialogWidth.Medium);

        filledCut.WaitForAssertion(() => filledCut.Find("form").Should().NotBeNull());
        filledCut.Find("form").Submit();

        var filledResult = await filledTask;
        filledResult.Should().NotBeNull();
        filledResult!.Kind.Should().Be(InstructionKindDto.Image);
        filledResult.Title.Should().Be("Diagram", "the seeded title round-trips (C2)");
        filledResult.Body.Should().Be("Label the parts.", "…and so does the seeded body");
        filledResult.Url.Should().BeNull("a media result carries no url");
    }

    // ── Post-close rework (owner 2026-10-14, grill Q9-Q12): the Preview ⇄ Description toggle ──────

    private static Func<Task<string?>> LoaderReturning(string? url, Action? onCall = null) =>
        () =>
        {
            onCall?.Invoke();
            return Task.FromResult(url);
        };

    /// <summary>The toggle is a lazy SEAM, not a fixture: with no loader the media dialog renders the
    /// classic Title + Description pair and NO toggle — never a dead button (the owner rule).</summary>
    [TestMethod]
    public async Task MediaDialog_WithoutAPreviewLoader_RendersNoToggle()
    {
        var cut = RenderProvider();
        _ = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            MaterialEditFormModel.FromRow(new InstructionEditorRow
            {
                Kind = InstructionKindDto.Video,
                FileName = "clip.mp4",
                ContentType = "video/mp4",
            }),
            "Edit Material", DialogWidth.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.FindAll("form fluent-text-input").Should().HaveCount(1,
            "the Title field renders; the Description is the shared rich editor");
        cut.FindComponents<RichInstructionEditor>().Should().ContainSingle(
            e => e.Instance.Id == MaterialEditDialog.BodyFieldId,
            "…and the Description slot holds the same rich editor a Text material's body uses");
        cut.FindAll("fluent-button").Should().NotContain(b => b.TextContent.Trim() == "Preview");
    }

    /// <summary>The swap itself: pressing Preview resolves the URL ONCE through the seam, replaces the
    /// description field with the native player in the SAME slot (the title stays), and Hide preview
    /// brings the description back. Re-showing resolves nothing new — the URL is cached per dialog.</summary>
    [TestMethod]
    public async Task MediaDialog_PreviewToggle_SwapsTheDescriptionSlotForThePlayer()
    {
        var calls = 0;
        var cut = RenderProvider();
        _ = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            new MaterialEditFormModel
            {
                Kind = InstructionKindDto.Video,
                PreviewLoader = LoaderReturning("blob:http://localhost/abc", () => calls++),
            },
            "Edit Material", DialogWidth.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        var show = cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Preview");
        await cut.InvokeAsync(() => show.Click());

        cut.WaitForAssertion(() => cut.Find("video").GetAttribute("src").Should().Be("blob:http://localhost/abc"));
        cut.FindAll("form fluent-text-input").Should().HaveCount(1,
            "the description slot is occupied by the preview; the title stays");
        cut.FindComponents<RichInstructionEditor>().Should().BeEmpty(
            "the preview OWNS the slot — the editor is unmounted, not merely hidden");

        var hide = cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Hide preview");
        await cut.InvokeAsync(() => hide.Click());
        cut.FindAll("video").Should().BeEmpty("the preview leaves the slot when hidden");
        cut.FindComponents<RichInstructionEditor>().Should().ContainSingle(
            "the description editor returns to the slot when the preview is hidden");

        var showAgain = cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Preview");
        await cut.InvokeAsync(() => showAgain.Click());
        cut.WaitForAssertion(() => cut.Find("video").Should().NotBeNull());
        calls.Should().Be(1, "the URL resolves once per dialog, not once per toggle");
    }

    /// <summary>A seam that cannot produce bytes surfaces the NAMED state — not a blank box, not a
    /// crash (the AC-10 "named error rather than dead affordance" rule, applied to the preview).</summary>
    [TestMethod]
    public async Task MediaDialog_PreviewLoaderReturnsNull_ShowsTheUnavailableState()
    {
        var cut = RenderProvider();
        _ = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            new MaterialEditFormModel { Kind = InstructionKindDto.Image, PreviewLoader = LoaderReturning(null) },
            "Edit Material", DialogWidth.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        var show = cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Preview");
        await cut.InvokeAsync(() => show.Click());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Preview unavailable for this file."));
        cut.FindAll("img").Should().BeEmpty();
    }

    /// <summary>Kind-faithful rendering: an Image preview is the native img — the same element the
    /// ward surface uses, so the preview never lies about the media. Rendered in its OWN provider root,
    /// because in v5 every <c>FluentDialogProvider</c> root renders the content of EVERY dialog open in
    /// the shared service — probe-verified: a second root re-renders the first dialog's body as a fresh
    /// instance — so two kinds sharing one root leave two identical "Preview" toggles with no way to
    /// name the intended subject.</summary>
    [TestMethod]
    public async Task MediaDialog_PreviewRendersTheKindNativeElement()
    {
        var imageCut = RenderProvider();
        _ = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            new MaterialEditFormModel { Kind = InstructionKindDto.Image, PreviewLoader = LoaderReturning("blob:http://localhost/img") },
            "Edit Material", DialogWidth.Medium);

        imageCut.WaitForAssertion(() => imageCut.Find("form").Should().NotBeNull());
        // The toggle is named by its container plus its own aria-label, not by the word "Preview":
        // the dialog's buttons include a close button and the footer pair, and the aria-label flips
        // with the toggle's state ("Preview the media file" ⇄ "Hide the media preview").
        var showImage = imageCut.FindAll(".media-preview-toggle fluent-button")
            .Single(b => b.GetAttribute("aria-label") == "Preview the media file");
        await imageCut.InvokeAsync(() => showImage.Click());
        imageCut.WaitForAssertion(() => imageCut.Find("img").GetAttribute("src").Should().Be("blob:http://localhost/img"));
        imageCut.FindAll("video").Should().BeEmpty();
    }

    /// <summary>Kind-faithful rendering, audio half of the pair above — its own provider root for the
    /// reason given there (a second root would also render the image dialog's freshly-instanced body).</summary>
    [TestMethod]
    public async Task MediaDialog_PreviewRendersTheAudioElement()
    {
        var audioCut = RenderProvider();
        _ = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            new MaterialEditFormModel { Kind = InstructionKindDto.Audio, PreviewLoader = LoaderReturning("blob:http://localhost/aud") },
            "Edit Material", DialogWidth.Medium);

        audioCut.WaitForAssertion(() => audioCut.Find("form").Should().NotBeNull());
        var showAudio = audioCut.FindAll(".media-preview-toggle fluent-button")
            .Single(b => b.GetAttribute("aria-label") == "Preview the media file");
        await audioCut.InvokeAsync(() => showAudio.Click());
        audioCut.WaitForAssertion(() => audioCut.Find("audio").GetAttribute("src").Should().Be("blob:http://localhost/aud"));
        audioCut.FindAll("video").Should().BeEmpty();
    }

    /// <summary>Post-close correction (owner 2026-10-14): a media description is authored in the shared
    /// rich editor, so the submit path is the Text branch's body half — read from the editor at submit
    /// and sanitised on the way out. A stored script can never ride along in a media payload either.</summary>
    [TestMethod]
    public async Task Media_DescriptionHtml_IsReadAtSubmit_AndSanitisedOnWrite()
    {
        JSInterop.Setup<string>("QuillFunctions.getQuillHTML", _ => true)
            .SetResult("<p>Label <strong>the parts</strong>.</p><script>alert('x')</script>");
        var cut = RenderProvider();
        var task = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            MaterialEditFormModel.FromRow(new InstructionEditorRow
            {
                Kind = InstructionKindDto.Image,
                FileName = "diagram.png",
                ContentType = "image/png",
                Title = "Diagram",
                Text = "stale seeded body",
            }),
            "Edit Material", DialogWidth.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        var result = await task;
        result.Should().NotBeNull("a media row's metadata is always optional — nothing can refuse it");
        result!.Title.Should().Be("Diagram");
        result.Body.Should().Contain("<strong>the parts</strong>",
            "the description is read from the editor at submit, exactly like a Text body");
        result.Body.Should().NotContain("<script",
            "…and sanitised on the way out, exactly like a Text body");
    }

    /// <summary>The interaction the preview toggle creates: the editor UNMOUNTS while the preview owns
    /// the slot, so the dialog captures the author's HTML on the way in. Both orders must submit the
    /// EDIT — hiding the preview first, or submitting while the preview still shows.</summary>
    [TestMethod]
    public async Task Media_EditedDescription_SurvivesThePreviewToggle_AndSubmitsEitherWay()
    {
        JSInterop.Setup<string>("QuillFunctions.getQuillHTML", _ => true)
            .SetResult("<p>Edited after toggling.</p>");

        // ── Order 1: edit → preview → hide → submit. ──
        var hideCut = RenderProvider();
        var hideTask = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            new MaterialEditFormModel { Kind = InstructionKindDto.Audio, PreviewLoader = LoaderReturning("blob:http://localhost/a") },
            "Edit Material", DialogWidth.Medium);

        hideCut.WaitForAssertion(() => hideCut.Find("form").Should().NotBeNull());
        var toPreview = hideCut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Preview");
        await hideCut.InvokeAsync(() => toPreview.Click());
        hideCut.WaitForAssertion(() => hideCut.Find("audio").Should().NotBeNull());
        var toDescription = hideCut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Hide preview");
        await hideCut.InvokeAsync(() => toDescription.Click());
        hideCut.WaitForAssertion(() => hideCut.FindComponents<RichInstructionEditor>().Should().ContainSingle());
        hideCut.Find("form").Submit();

        var hidden = await hideTask;
        hidden.Should().NotBeNull();
        hidden!.Body.Should().Be("<p>Edited after toggling.</p>",
            "the description captured on the way into the preview comes back for the submit");

        // ── Order 2: edit → preview → submit while the preview still shows. ──
        var showCut = RenderProvider();
        var showTask = DialogService.ShowShellDialogAsync<MaterialEditDialog, MaterialEditFormModel, MaterialEditResult>(
            new MaterialEditFormModel { Kind = InstructionKindDto.Video, PreviewLoader = LoaderReturning("blob:http://localhost/v") },
            "Edit Material", DialogWidth.Medium);

        showCut.WaitForAssertion(() => showCut.Find("form").Should().NotBeNull());
        var preview = showCut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Preview");
        await showCut.InvokeAsync(() => preview.Click());
        showCut.WaitForAssertion(() => showCut.Find("video").Should().NotBeNull());
        showCut.Find("form").Submit();

        var shown = await showTask;
        shown.Should().NotBeNull();
        shown!.Body.Should().Be("<p>Edited after toggling.</p>",
            "submitting from the preview path sanitises the captured HTML instead of dropping the edit");
    }
}
