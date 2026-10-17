using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// QR-5 (spec §5.2/§5.6): the shared inline instruction editor — one shape, two owners. The media
/// pipeline is driven through its testable seam (the <c>ResourcesSection.StageFileAsync</c>
/// precedent), because FluentInputFile's JS pipeline is not up in bUnit.
/// </summary>
[TestClass]
public class InstructionEditorListBunitTests : BunitContext
{
    public InstructionEditorListBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private IRenderedComponent<InstructionEditorList> Render(
        List<InstructionEditorRow> rows, StageInstructionMediaAsync? stage = null,
        LoadInstructionMediaAsync? load = null) =>
        Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.StageMediaAsync, stage)
            .Add(c => c.LoadInstructionMediaAsync, load));

    private static StageInstructionMediaAsync Returning(StagedAttachmentDto? staged, Action? onCall = null) =>
        (row, content, name, type, size) =>
        {
            onCall?.Invoke();
            return Task.FromResult(staged);
        };

    /// <summary>Post-close rework (owner 2026-10-14, grill Q11): the dialog-preview seam. The loader
    /// exists ONLY when bytes can actually resolve — a persisted row with the host's served-fetch
    /// seam gets one (lazy: streams on the first press); an unsaved row with nothing staged, or with
    /// no seam, gets NONE, so the dialog renders no toggle rather than a dead one.</summary>
    [TestMethod]
    public void GetPreviewLoader_OnlyExistsWhenBytesCanResolve()
    {
        var saved = new InstructionEditorRow { Id = Guid.NewGuid(), Kind = InstructionKindDto.Video, ContentType = "video/mp4" };
        var unsaved = new InstructionEditorRow { Kind = InstructionKindDto.Audio };
        var rows = new List<InstructionEditorRow> { saved, unsaved };

        var withoutSeam = Render(rows);
        withoutSeam.Instance.GetPreviewLoader(saved).Should().BeNull("an unsaved row has no bytes to preview, and no seam to fetch them");
        withoutSeam.Instance.GetPreviewLoader(unsaved).Should().BeNull();

        var withSeam = Render(rows, load: _ => Task.FromResult<Stream?>(new MemoryStream()));
        withSeam.Instance.GetPreviewLoader(saved).Should().NotBeNull("a persisted row can stream its bytes on demand");
        withSeam.Instance.GetPreviewLoader(unsaved).Should().BeNull("still nothing staged and nothing to serve for the unsaved row");
    }

    [TestMethod]
    public void ReadOnly_RendersTheBlocksWithoutAffordances()
    {
        var rows = new List<InstructionEditorRow>
        {
            new() { Kind = InstructionKindDto.Text, Text = "Read it twice." },
        };
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.ReadOnly, true));

        cut.FindAll("#instr-add").Should().BeEmpty("View mode offers no add affordance");
        cut.FindAll("#instr-remove-0").Should().BeEmpty("…and no destructive one either");
        cut.Markup.Should().Contain("Read it twice.", "the block's payload is shown, not editable");
    }

    [TestMethod]
    public void Empty_RendersTheEmptyStateAndTheAddAffordance()
    {
        var cut = Render([]);

        cut.FindAll("#instr-empty").Should().ContainSingle();
        cut.Find("#instr-add").Should().NotBeNull();
    }

    [TestMethod]
    public void Add_AppendsATextRow_AndDropsTheEmptyState()
    {
        var rows = new List<InstructionEditorRow>();
        var cut = Render(rows);

        cut.Find("#instr-add").Click();

        rows.Should().ContainSingle().Which.Kind.Should().Be(InstructionKindDto.Text);
        cut.FindAll("#instr-empty").Should().BeEmpty();
    }

    [TestMethod]
    public void Remove_DropsTheRow()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Text } };
        var cut = Render(rows);

        cut.Find("#instr-remove-0").Click();

        rows.Should().BeEmpty();
    }

    [TestMethod]
    public void KindSwitch_RendersThePayloadThatKindNeeds()
    {
        var cut = Render([new InstructionEditorRow { Kind = InstructionKindDto.Url }]);

        cut.Markup.Should().Contain("https://", "a link row takes a URL, not free text");
    }

    /// <summary>Round media-pill-listing (C1/Q13 — THIRD repin, Audio→Image): an UNTITLED Image row
    /// keeps today's size-text readout (Q13), so this pin stays meaningful where the Audio size text was
    /// retired into the duration readout. No clear-file × — the row's trailing remove × is the single
    /// close.</summary>
    [TestMethod]
    public void StagedMedia_RendersTheSizeText_WithNoClearAction()
    {
        var cut = Render(
        [
            new InstructionEditorRow
            {
                Kind = InstructionKindDto.Image,
                FileName = "diagram.png",
                ContentType = "image/png",
                FileSize = 2048,
                StoragePath = "tenants/t/staging/g/diagram.png",
            },
        ]);

        cut.Markup.Should().Contain(AttachmentUploadPolicy.FormatFileSize(2048),
            "an untitled Image keeps the size-text readout (Q13)");
        cut.FindAll("#instr-clear-media-0").Should().BeEmpty(
            "the clear-file × is retired — one close per row (owner, 2026-10-12)");
        cut.Find("#instr-remove-0").Should().NotBeNull(
            "the row's remove × is that single close");
    }

    [TestMethod]
    public async Task StageMediaFile_Success_FillsTheRowFromTheHostsResult()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Audio } };
        var cut = Render(rows, Returning(
            new StagedAttachmentDto("how-to.mp3", "audio/mpeg", 3, "tenants/t/staging/g/how-to.mp3")));

        await cut.InvokeAsync(() => cut.Instance.StageMediaFileAsync(
            rows[0], new MemoryStream([1, 2, 3]), "how-to.mp3", "audio/mpeg", 3));

        rows[0].StoragePath.Should().Be("tenants/t/staging/g/how-to.mp3");
        rows[0].FileName.Should().Be("how-to.mp3");
        rows[0].ContentType.Should().Be("audio/mpeg");
        cut.Markup.Should().NotContain("could not be attached", "a successful stage reports no failure");
    }

    [TestMethod]
    public async Task StageMediaFile_ClientPolicyRejection_NeverCallsTheHost()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Image } };
        var called = false;
        var cut = Render(rows, Returning(staged: null, onCall: () => called = true));

        await cut.InvokeAsync(() => cut.Instance.StageMediaFileAsync(
            rows[0], new MemoryStream([1]), "payload.exe", "application/octet-stream", 1));

        called.Should().BeFalse("the client pre-check refuses it before any round trip");
        cut.Markup.Should().Contain("not allowed");
        rows[0].StoragePath.Should().BeNull();
    }

    [TestMethod]
    public async Task StageMediaFile_AudioRidesTheWidenedAllowList()
    {
        // Round Q1 (owner, 2026-10-09): the media extensions joined the shared allow-list, so an
        // audio instruction file passes the same pre-check a document does — before that, audio and
        // video could not be attached at all.
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Audio } };
        var called = false;
        var cut = Render(rows, Returning(
            new StagedAttachmentDto("how-to.mp3", "audio/mpeg", 3, "tenants/t/staging/g/how-to.mp3"),
            onCall: () => called = true));

        await cut.InvokeAsync(() => cut.Instance.StageMediaFileAsync(
            rows[0], new MemoryStream([1, 2, 3]), "how-to.mp3", "audio/mpeg", 3));

        called.Should().BeTrue("an audio file must reach the host rather than be refused client-side");
        rows[0].StoragePath.Should().Be("tenants/t/staging/g/how-to.mp3");
    }

    [TestMethod]
    public async Task StageMediaFile_HostRefusal_AsksTheAuthorToRetry()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Image } };
        var cut = Render(rows, Returning(staged: null));

        await cut.InvokeAsync(() => cut.Instance.StageMediaFileAsync(
            rows[0], new MemoryStream([1]), "diagram.png", "image/png", 1));

        cut.Markup.Should().Contain("could not be attached");
    }

    [TestMethod]
    public async Task StageMediaFile_WithoutAHostSeam_SaysSo()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Image } };
        var cut = Render(rows);

        await cut.InvokeAsync(() => cut.Instance.StageMediaFileAsync(
            rows[0], new MemoryStream([1]), "diagram.png", "image/png", 1));

        cut.Markup.Should().Contain("not available here");
    }

    // ── the stray-brace regression (screenshot report, 2026-10-09) ────────────────────────────
    // The error row's `@if (_error is not null)` guard had been left ABOVE the `@code` block while
    // its body sat below it. Razor still compiled, emitting the `{`/`}` as literal markup and the
    // error <p> UNCONDITIONALLY — a lone "}" rendered on the authoring page. These three assertions
    // pin the structure from the DOM side: nothing stray, nothing unconditional, one row on failure.

    /// <summary>The error row is conditional — an empty editor renders none.</summary>
    [TestMethod]
    public void ErrorRow_IsAbsent_UntilSomethingFails()
    {
        var cut = Render([new InstructionEditorRow { Kind = InstructionKindDto.Text }]);

        cut.FindAll("#instr-error").Should().BeEmpty(
            "no failure has happened yet — an always-rendered error row is the stray-brace defect");
    }

    /// <summary>No literal braces leak into the DOM: an orphaned block would render its own braces.</summary>
    [TestMethod]
    public void Markup_CarriesNoStrayBraces()
    {
        var cut = Render([new InstructionEditorRow { Kind = InstructionKindDto.Text }]);

        // FluentUI's own components emit <style> elements, whose rule bodies legitimately contain
        // braces — stripped first, because they are the library's markup, not ours.
        var markup = Regex.Replace(cut.Markup, "(?s)<style.*?</style>", string.Empty);

        markup.Should().NotContain("{").And.NotContain("}",
            "an orphaned @if body renders its braces as literal text — the visible '}' bug");
    }

    /// <summary>…and the row still appears exactly once when a failure does happen.</summary>
    [TestMethod]
    public async Task ErrorRow_RendersOnce_WhenSomethingFails()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Image } };
        var cut = Render(rows);

        await cut.InvokeAsync(() => cut.Instance.StageMediaFileAsync(
            rows[0], new MemoryStream([1]), "diagram.png", "image/png", 1));

        cut.FindAll("#instr-error").Should().ContainSingle();
        cut.Markup.Should().Contain("not available here");
    }

    // ── Round material-row-editing: the RowEditingMode seam (Q4(b), AC-14/AC-15/AC-16) ─────────

    /// <summary>AC-14: in Dialog mode the row IDENTIFIES the material — its name, no field of its own —
    /// and offers the affordance the host hangs its dialog off. The body is deliberately absent: it is
    /// read and edited in the dialog (owner, 2026-10-10).</summary>
    [TestMethod]
    public void DialogMode_IdentifiesTheRowByItsName_WithNoFields()
    {
        var rows = new List<InstructionEditorRow>
        {
            new() { Kind = InstructionKindDto.Text, Title = "Fraction basics", Text = "Read pages 4-6." },
        };
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog));

        cut.Markup.Should().Contain("Fraction basics", "the row identifies the material by its name");
        cut.Markup.Should().NotContain("Read pages 4-6.",
            "the body belongs to the dialog now — the list identifies, it does not read");
        cut.Find(".cq-instruction-kind").GetAttribute("aria-label").Should().Be("Text",
            "Q3: the indicator is an icon in this mode too, and still names its kind");
        cut.FindAll("fluent-text-input").Should().BeEmpty(
            "the dialog is the edit path, so the row renders no field");
        cut.Find("#instr-edit-0").Should().NotBeNull("…and the affordance that opens it");
    }

    /// <summary>AC-14's Link rule: a link with a label is named by it, and a link with NO label is named
    /// by its target — the row is never blank (the ward card's own title ?? url rule, D11).</summary>
    [TestMethod]
    public void DialogMode_LinkWithNoLabel_IsNamedByItsUrl()
    {
        var rows = new List<InstructionEditorRow>
        {
            new() { Kind = InstructionKindDto.Url, Url = "https://khan.example/fractions" },
            new() { Kind = InstructionKindDto.Url, Url = "https://khan.example/decimals", Title = "Decimals" },
        };
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog));

        cut.Find("#instr-label-0").TextContent.Should().Be("https://khan.example/fractions",
            "no label → the target names the link");
        cut.Find("#instr-label-1").TextContent.Should().Be("Decimals",
            "a label, when there is one, wins");
    }

    /// <summary>AC-14: activating the affordance asks the HOST to edit — the component owns no dialog,
    /// and it carries its own row rather than the first or last.</summary>
    [TestMethod]
    public void DialogMode_EditAffordance_RaisesTheEditRequestForItsOwnRow()
    {
        var rows = new List<InstructionEditorRow>
        {
            new() { Kind = InstructionKindDto.Url, Url = "https://example.test/a", Title = "A" },
            new() { Kind = InstructionKindDto.Url, Url = "https://example.test/b", Title = "B" },
        };
        InstructionEditorRow? requested = null;
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog)
            .Add(c => c.EditRowRequested, (InstructionEditorRow row) => requested = row));

        cut.Find("#instr-edit-1").Click();

        requested.Should().BeSameAs(rows[1], "the affordance carries its own row");
    }

    /// <summary>AC-16: Inline is the DEFAULT, so the question dialog's host — which cannot open a
    /// dialog of its own (no nesting) — keeps editing in the row without opting in.</summary>
    [TestMethod]
    public void InlineIsTheDefault_SoTheQuestionHostKeepsItsFields()
    {
        var rows = new List<InstructionEditorRow>
        {
            new() { Kind = InstructionKindDto.Text, Title = "Named." },
        };
        var cut = Render(rows);

        cut.FindAll("#instr-edit-0").Should().BeEmpty(
            "AC-16: no edit affordance — the question host has no dialog to open");
        cut.FindAll("fluent-text-input").Should().NotBeEmpty("…it still edits inline");
    }

    /// <summary>Grill Q3 as reworked by round media-pill-listing (C2 — SECOND repin): media rows now
    /// edit THROUGH the readout-opened dialog, so the row carries the readout (the edit affordance) and
    /// no pencil — the file fields are still not inline-editable and never appear in the dialog.</summary>
    [TestMethod]
    public void DialogMode_LeavesMediaRowsOnTheStagedFileFlow()
    {
        var rows = new List<InstructionEditorRow>
        {
            new()
            {
                Kind = InstructionKindDto.Audio,
                FileName = "clip.mp3",
                ContentType = "audio/mpeg",
                FileSize = 1024,
                StoragePath = "staged/clip.mp3",
            },
        };
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog));

        cut.FindAll("#instr-edit-0").Should().BeEmpty("the pencil never appears on a media row (C1)");
        cut.FindAll(".cq-media-readout").Should().ContainSingle(
            "the readout IS the media row's edit affordance — clicking it opens the host's dialog (C2)");
        cut.FindAll(".cq-instruction-file").Should().BeEmpty(
            "the old size-text span is retired into the readout");
        cut.FindAll("#instr-clear-media-0").Should().BeEmpty(
            "the clear-file × is retired — the row's remove × is the single close");
        cut.Find("#instr-remove-0").Should().NotBeNull("…and the staged-file flow keeps its exit");
    }

    /// <summary>AC-14 (write-back half): the applied result lands on the row through the LIST — it stays
    /// the single writer of its rows — and the host is told (Changed), so its gate re-runs.</summary>
    [TestMethod]
    public async Task ApplyEdit_WritesTheResultOntoTheRow_AndRaisesChanged()
    {
        var rows = new List<InstructionEditorRow>
        {
            new() { Kind = InstructionKindDto.Text, Title = "Old name", Text = "Old body" },
        };
        var changed = 0;
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog)
            .Add(c => c.Changed, () => changed++));

        await cut.InvokeAsync(() => cut.Instance.ApplyEditAsync(
            rows[0], new MaterialEditResult(InstructionKindDto.Text, "New name", "New body", null)));

        rows[0].Title.Should().Be("New name");
        rows[0].Text.Should().Be("New body");
        rows[0].Url.Should().BeNull("a Text result carries no url");
        changed.Should().Be(1, "the host re-renders its counts and re-checks its submit gate");
        cut.Markup.Should().Contain("New name", "and the read-out shows the applied values");
    }
    /// <summary>Grill Q1/Q2/Q4 (round material-row-editing): the kind reads as an ICON — the word is gone
    /// from the row — and it survives only as the wrapper's accessible name, because an icon-only
    /// indicator with no name would be an a11y regression. Also pins the glyph, and the mode
    /// independence Q3 decided: this renders the Inline default (the Dialog read-out is asserted in
    /// <c>DialogMode_IdentifiesTheRowByItsName_WithNoFields</c>).</summary>
    [TestMethod]
    public void Kind_IsAnIcon_WithTheWordAsItsAccessibleName()
    {
        var rows = new List<InstructionEditorRow>
        {
            new() { Kind = InstructionKindDto.Text, Title = "Fraction basics" },
            new() { Kind = InstructionKindDto.Audio, FileName = "clip.mp3", FileSize = 1024, StoragePath = "p" },
        };
        var cut = Render(rows);

        var labels = cut.FindAll(".cq-instruction-kind");
        labels.Should().HaveCount(2, "every row still names its kind");
        labels.Select(l => l.GetAttribute("aria-label")).Should().Equal(["Text", "Audio"],
            "the word survives as the accessible name");
        labels.Select(l => l.GetAttribute("title")).Should().Equal(["Text", "Audio"],
            "…and as the tooltip, so hover stays honest");
        labels.Should().OnlyContain(l => l.TextContent.Trim().Length == 0,
            "the kind is an ICON now — no 'Text'/'Audio' word renders in the row");
        labels.Should().OnlyContain(l => l.Children.Length > 0, "…and the glyph itself renders");
    }

    // ── Recording materials (spec recording-materials D2/D3/D10) ──────────────────────────────

    [TestMethod]
    public void EmptyMediaRow_WithAStagingSeam_ShowsTheRecordAffordance()
    {
        // AC-1/AC-6: the Record affordance sits in the media row's capture slot beside the upload control
        // — a recording is a second input path to the same staged media row.
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Video } };
        var cut = Render(rows, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")));

        cut.Find("#instr-record-0").Should().NotBeNull(
            "a media row with a staging seam offers Record alongside Upload From Device");
    }

    [TestMethod]
    public void EmptyMediaRow_WithoutAStagingSeam_HidesTheRecordAffordance()
    {
        // The negative side of the same conditional (testing.md §"Assert the negative"): with no host
        // seam, upload could never succeed, so Record is not offered either — never a control that fails.
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Video } };
        var cut = Render(rows, stage: null);

        cut.FindAll("#instr-record-0").Should().BeEmpty(
            "without a staging seam the Record affordance is not rendered");
    }

    [TestMethod]
    public void AddRowWithRecorder_OpensTheRecorderOnTheAppendedRow()
    {
        // AC-1/AC-6: the page's "Record Video" kebab appends a Video row and opens its inline recorder in
        // one step. The recorder panel is inline (not a dialog) so it works inside the question surface too.
        var rows = new List<InstructionEditorRow>();
        var cut = Render(rows, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")));

        cut.InvokeAsync(() => cut.Instance.AddRowWithRecorderAsync(InstructionKindDto.Video));

        rows.Should().ContainSingle().Which.Kind.Should().Be(InstructionKindDto.Video);
        cut.FindComponents<RecorderPanel>().Should().ContainSingle(
            "the appended row's inline recorder is open");
    }

    /// <summary>Round-recorder-takeover AC-1/AC-2: while a recorder is open the list body — rows,
    /// dropzone, add button, row Record affordances — is NOT rendered; the takeover frame + panel
    /// fill the container instead, and the container carries the recording marker class.</summary>
    [TestMethod]
    public void OpenRecorder_TakesOverTheWholeContainer()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Video } };
        var cut = Render(rows, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")));

        cut.Find("#instr-record-0").Click();

        cut.FindAll("#instr-rows").Should().BeEmpty("the row list is suppressed during the takeover");
        cut.FindAll("fluent-input-file").Should().BeEmpty("…as is the upload dropzone");
        cut.FindAll("#instr-add").Should().BeEmpty("…and the add affordance");
        cut.FindAll("#instr-record-0").Should().BeEmpty("row Record buttons go with the rows");
        cut.Find("#instr-recorder-takeover").Should().NotBeNull(
            "the takeover frame replaces the list body");
        cut.Find("#instr-recorder-0").Should().NotBeNull(
            "the panel renders inside it, id'd at its row's index");
        cut.Find(".cq-instructions").ClassList.Should().Contain("cq-instructions-recording",
            "the container class marks the recording posture for styling");
    }

    /// <summary>AC-3 (Q5): the panel's Cancel restores the list — rows, add button, the row's Record
    /// affordance — drops the takeover and the container class; cancel discards the take. A row the
    /// author toggled the recorder OPEN on pre-exists the record action, so it is never dropped
    /// (owner rework 2026-10-12 — contrast CancelRecorder_RemovesTheRowTheRecordActionAppended).</summary>
    [TestMethod]
    public void CancelRecorder_RestoresTheListAndKeepsTheToggledRow()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Video } };
        var cut = Render(rows, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")));

        cut.Find("#instr-record-0").Click();
        cut.Find("#instr-recorder-0-cancel").Click();

        cut.FindAll("#instr-recorder-takeover").Should().BeEmpty("Cancel closes the takeover");
        cut.Find("#instr-rows").Should().NotBeNull("the list body comes back");
        cut.Find("#instr-add").Should().NotBeNull("…and so does the add affordance");
        cut.Find("#instr-record-0").Should().NotBeNull(
            "the row keeps its Record affordance");
        cut.Find(".cq-instructions").ClassList.Should().NotContain("cq-instructions-recording",
            "…and the container is out of its recording posture");
        rows.Should().ContainSingle(
            "a toggled-open row pre-exists the record action — cancel discards the take, not the row");
    }

    /// <summary>Owner rework (2026-10-12): Cancel after the kebab's Record action must not leave
    /// recording residue — the placeholder row the action itself appended is dropped together with
    /// the takeover, and the empty state returns.</summary>
    [TestMethod]
    public async Task CancelRecorder_RemovesTheRowTheRecordActionAppended()
    {
        var rows = new List<InstructionEditorRow>();
        var cut = Render(rows, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")));

        await cut.InvokeAsync(() => cut.Instance.AddRowWithRecorderAsync(InstructionKindDto.Video));
        cut.Find("#instr-recorder-0-cancel").Click();

        rows.Should().BeEmpty("the record action's own placeholder is residue — Cancel drops it");
        cut.FindAll("#instr-recorder-takeover").Should().BeEmpty("the takeover is closed");
        cut.Find(".cq-instructions-empty").Should().NotBeNull(
            "the empty state returns with the list");
    }

    /// <summary>One-recorder invariant (one camera/mic, Q5) + owner rework (2026-10-12): a second
    /// AddRowWithRecorderAsync MOVES the takeover to the newly appended row — never a second panel —
    /// and the first record-action placeholder (still unstaged) is dropped, so no ghost row remains.</summary>
    [TestMethod]
    public async Task SecondAddRowWithRecorder_MovesTheTakeover_AndDropsTheFirstPlaceholder()
    {
        var rows = new List<InstructionEditorRow>();
        var cut = Render(rows, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")));

        await cut.InvokeAsync(() => cut.Instance.AddRowWithRecorderAsync(InstructionKindDto.Audio));
        await cut.InvokeAsync(() => cut.Instance.AddRowWithRecorderAsync(InstructionKindDto.Video));

        rows.Should().ContainSingle("the first placeholder is residue and must not survive the switch")
            .Which.Kind.Should().Be(InstructionKindDto.Video);
        cut.FindComponents<RecorderPanel>().Should().ContainSingle(
            "one camera/mic — a second recorder must never coexist with the first");
        cut.Find("#instr-recorder-takeover").Should().NotBeNull("the takeover MOVES with the recorder");
        cut.Find("#instr-recorder-0").Should().NotBeNull(
            "the panel id follows the appended row's (now sole) index");
    }

    /// <summary>Owner rework (2026-10-12): the host must learn when a recorder opens/closes — the
    /// authoring page's materials dropzone sits outside this component and hides on the open signal.</summary>
    [TestMethod]
    public async Task RecorderState_Changes_AreSignalledToTheHost()
    {
        var rows = new List<InstructionEditorRow>();
        var states = new List<bool>();
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.StageMediaAsync, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")))
            .Add(c => c.RecorderStateChanged, (bool open) => states.Add(open)));

        await cut.InvokeAsync(() => cut.Instance.AddRowWithRecorderAsync(InstructionKindDto.Video));
        cut.Find("#instr-recorder-0-cancel").Click();

        states.Should().Equal(new[] { true, false }, "open signalled by the kebab action, closed by Cancel");
    }

    /// <summary>Owner pill rework (2026-10-12), as REPINNED by round media-pill-listing (C1 — repin 1):
    /// a committed take's session Blob URL + duration ride the commit seam into the row's READOUT —
    /// `2:34` — and the PLAY button reopens the SAME review panel (player + Record again, no commit
    /// button). Closing keeps the entry; the row survives.</summary>
    [TestMethod]
    public async Task RecordedRow_Pill_ShowsDuration_AndReopensTheReviewPlayer()
    {
        var rows = new List<InstructionEditorRow>
        {
            new() { Kind = InstructionKindDto.Video },
        };
        var cut = Render(rows, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")));

        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(Stream.Null, "v.webm", "video/webm", 10, "blob:test-take", 64)));

        rows[0].StoragePath.Should().Be("p", "the take staged through the same seam an upload takes");
        cut.Find("#instr-title-0").TextContent.Should().Contain("1:04",
            "the readout carries the take's probed duration (C1 — the pill is retired)");
        cut.FindAll("#instr-clear-media-0").Should().BeEmpty("no clear-file × (retired)");

        cut.Find("#instr-play-0").Click();

        cut.Find("#instr-recorder-takeover").Should().NotBeNull("the pill reopens the review takeover");
        cut.Find("#instr-recorder-0-preview-take").Should().NotBeNull("the SAME player renders");
        cut.Find("#instr-recorder-0-retake").Should().NotBeNull(
            "Record again lives on the review panel, never on the listed item");
        cut.FindAll("#instr-recorder-0-commit").Should().BeEmpty(
            "a reopened take has nothing new to commit — it is already staged");

        cut.Find("#instr-recorder-0-cancel").Click();

        cut.FindAll("#instr-recorder-takeover").Should().BeEmpty("closing restores the list");
        cut.Find("#instr-play-0").Should().NotBeNull(
            "the session entry survives a close — the pill can reopen it");
    }

    /// <summary>Owner defect fix (2026-10-12): removing a row whose session URL cannot be revoked (the
    /// media module is missing or its call fails — e.g. a session predating the new asset) must NEVER
    /// blow up — revocation is best-effort, guarded as ONE try around import + call; the row removal
    /// is what matters.</summary>
    [TestMethod]
    public async Task RemoveRow_WhenTheMediaModuleFails_StillRemovesTheRow()
    {
        var rows = new List<InstructionEditorRow>
        {
            new() { Kind = InstructionKindDto.Video },
        };
        var cut = Render(rows, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")));
        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(Stream.Null, "v.webm", "video/webm", 10, "blob:doomed", 5)));

        // Simulate the failing module: the revoke call throws (the same one-line guard covers a failing
        // import — the stale-session 404 the owner hit).
        var module = JSInterop.SetupModule(
            "./_content/SchoolCollab.Assignments.Application/Components/Pages/Assignments/InstructionEditorList.razor.js");
        module.SetupVoid("revokeMediaUrl", _ => { throw new JSException("module call failed"); });

        await cut.InvokeAsync(() => cut.Instance.RemoveAsync(rows[0]));

        rows.Should().BeEmpty("the row is removed even though its URL could not be revoked");
        cut.FindAll("#instr-rows").Should().BeEmpty("…and the list re-rendered empty");
    }

    /// <summary>Hold verdict 2026-10-12 (finding 2): the kebab record action opening while a pill
    /// review is taken over must start a FRESH panel — both halves of the fix: the stale seed is
    /// cleared AND @key forces a new instance (clearing the seed alone left the reused instance's
    /// reopened-Review state; the key alone would have applied the stale seed).</summary>
    [TestMethod]
    public async Task KebabRecorder_AfterAnOpenPillReview_IsAFreshPanel()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Video } };
        var cut = Render(rows, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")));

        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(Stream.Null, "v.webm", "video/webm", 10, "blob:test-take", 64)));
        cut.Find("#instr-play-0").Click();
        cut.Find("#instr-recorder-0-preview-take").Should().NotBeNull(
            "the pill reopens the seeded review takeover (the precondition of the defect)");

        await cut.InvokeAsync(() => cut.Instance.AddRowWithRecorderAsync(InstructionKindDto.Video));

        cut.Find("#instr-recorder-takeover").Should().NotBeNull(
            "the kebab action still owns the takeover");
        cut.Markup.Should().NotContain("-preview-take",
            "the fresh recorder must not reopen the previous take's player (stale seed + reused instance was the defect)");
        cut.Markup.Should().NotContain("blob:test-take",
            "the stale take's URL left the markup with the discarded panel instance");
        // Loose mode probes to "unsupported" — the fresh panel reached its own capability branch.
        cut.WaitForAssertion(() => cut.Find("#instr-recorder-1-unsupported").Should().NotBeNull());
    }

    /// <summary>Hold verdict 2026-10-12 (finding 4): teardown (dialog close / in-page navigation)
    /// revokes the session-held take URL — the list previously disposed only the JS module and leaked
    /// every pill URL.</summary>
    [TestMethod]
    public async Task Dispose_RevokesEverySessionHeldTakeUrl()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Video } };
        var cut = Render(rows, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")));
        var module = JSInterop.SetupModule(
            "./_content/SchoolCollab.Assignments.Application/Components/Pages/Assignments/InstructionEditorList.razor.js");

        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(Stream.Null, "v.webm", "video/webm", 10, "blob:leaked", 5)));

        await cut.InvokeAsync(async () => await cut.Instance.DisposeAsync());

        module.VerifyInvoke("revokeMediaUrl");
    }

    // ── Round media-pill-listing (C1/C2, Slice F): the Text-row shape, the readout, served playback ──

    private const string MediaModulePath =
        "./_content/SchoolCollab.Assignments.Application/Components/Pages/Assignments/InstructionEditorList.razor.js";

    private static InstructionEditorRow StagedMediaRow(
        InstructionKindDto kind = InstructionKindDto.Video, string fileName = "clip.webm",
        string contentType = "video/webm", string? title = null, int? durationSeconds = null) => new()
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            FileName = fileName,
            ContentType = contentType,
            FileSize = 1024,
            StoragePath = "staged/clip.webm",
            Title = title,
            DurationSeconds = durationSeconds,
        };

    /// <summary>C1 (AC-1): a staged Audio/Video row with NO session entry renders the Text-row shape —
    /// the standard kind glyph AND the trailing remove × are kept, the action slot holds the PLAY icon
    /// button, and the size text is gone. Clicking play invokes the host's served-play seam and reopens
    /// the SAME review panel (player + Record again — never the commit button). Pre-fix: size text, no
    /// play button, no served fetch at all.</summary>
    [TestMethod]
    public async Task ServedRow_PlayButton_RendersWithoutASessionEntry_AndInvokesTheLoadSeam()
    {
        var rows = new List<InstructionEditorRow> { StagedMediaRow() };
        InstructionEditorRow? loaded = null;
        var module = JSInterop.SetupModule(MediaModulePath);
        module.Setup<string>("createMediaUrlFromStream", _ => true).SetResult("blob:served");
        module.Setup<double?>("mediaDuration", _ => true).SetResult(null);

        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.LoadInstructionMediaAsync, (InstructionEditorRow row) =>
            {
                loaded = row;
                return Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]));
            }));

        var play = cut.Find("#instr-play-0");
        play.GetAttribute("aria-label").Should().Be("Play");
        cut.FindAll(".cq-instruction-kind").Should().ContainSingle(
            "the standard leading kind glyph is kept (C1)");
        cut.Find("#instr-remove-0").Should().NotBeNull("…and so is the standard trailing remove ×");
        cut.FindAll(".cq-instruction-file").Should().BeEmpty(
            "the retired size text is gone from an Audio/Video row (C1)");
        cut.FindAll(".cq-media-play").Should().BeEmpty("no pill survives (C1/AC-1)");

        play.Click();

        loaded.Should().BeSameAs(rows[0], "play fetches through the host's served-play seam");
        cut.Find("#instr-recorder-takeover").Should().NotBeNull("the review reopens");
        cut.Find("#instr-recorder-0-preview-take").Should().NotBeNull("…with the served take's player");
        cut.FindAll("#instr-recorder-0-commit").Should().BeEmpty(
            "a served play has nothing new to commit — playback only");
    }

    /// <summary><b>REGRESSION pin, not discriminating</b> (Slice F): the staged Audio row's kind glyph,
    /// trailing remove × and file-name tooltip render today and must keep rendering — the pin exists so
    /// the retired pill shape cannot come back, not because pre-fix code failed it.</summary>
    [TestMethod]
    public void MediaRow_KeepsTheKindGlyphAndTheRemove_WithTheNameTooltip()
    {
        var cut = Render([StagedMediaRow(kind: InstructionKindDto.Audio, "how-to.mp3", "audio/mpeg")]);

        cut.Find(".cq-instruction-kind").GetAttribute("aria-label").Should().Be("Audio",
            "the kind glyph is unchanged (continuity, not discrimination)");
        cut.Find("#instr-remove-0").GetAttribute("aria-label").Should().Be("Remove instruction");
        cut.Find(".cq-media-readout").GetAttribute("title").Should().Be("how-to.mp3",
            "the file name rides the native title tooltip");
    }

    /// <summary>C2/Q13 (AC-2): the readout reads `2:34 [Title]` (ellipsis-truncated) when titled,
    /// `2:34` untitled with a known duration, `—:—` when the duration is unknown, a titled Image
    /// `[Title]` with no duration, and an untitled Image today's size text. Pre-fix: no readout at all.</summary>
    [TestMethod]
    public void MediaRow_Readout_ShowsTitleDurationEllipsis_OrTheDashFallback()
    {
        var cut = Render(
        [
            StagedMediaRow(kind: InstructionKindDto.Audio, "a.mp3", "audio/mpeg", title: "Week 1 intro", durationSeconds: 154),
            StagedMediaRow(kind: InstructionKindDto.Video, "b.webm", "video/webm", durationSeconds: 154),
            StagedMediaRow(kind: InstructionKindDto.Audio, "c.mp3", "audio/mpeg"),
            StagedMediaRow(kind: InstructionKindDto.Image, "d.png", "image/png", title: "Diagram"),
            StagedMediaRow(kind: InstructionKindDto.Image, "e.png", "image/png"),
        ]);

        cut.Find("#instr-title-0").TextContent.Should().Be("2:34 [Week 1 intro]",
            "a titled media row reads duration + bracketed title");
        cut.Find("#instr-title-1").TextContent.Should().Be("2:34", "untitled reads the duration alone");
        cut.Find("#instr-title-2").TextContent.Should().Be("—:—",
            "an unknown duration (a legacy row / failed probe) reads the dash fallback, never a guess");
        cut.Find("#instr-title-3").TextContent.Should().Be("[Diagram]",
            "a titled Image reads its title with NO duration (Q13)");
        cut.Find("#instr-title-4").TextContent.Should().Be(AttachmentUploadPolicy.FormatFileSize(1024),
            "an untitled Image keeps today's size-text readout (Q13)");
        cut.Find("#instr-title-0").ClassList.Should().Contain("cq-instruction-readout",
            "the readout carries the ellipsis-truncation rule");
    }

    /// <summary>C2 (AC-3): the READOUT is the edit affordance — clicking it raises
    /// <see cref="InstructionEditorList.EditRowRequested"/> with its own row (the host opens the
    /// metadata dialog), while the PLAY button is purely preview and never opens it.</summary>
    [TestMethod]
    public void MediaRow_Readout_ClickRequestsTheEdit()
    {
        var rows = new List<InstructionEditorRow>
        {
            StagedMediaRow(durationSeconds: 154, title: "Intro"),
            StagedMediaRow(fileName: "other.webm"),
        };
        InstructionEditorRow? requested = null;
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog)
            .Add(c => c.EditRowRequested, (InstructionEditorRow row) => requested = row));

        cut.Find("#instr-title-1").Click();

        requested.Should().BeSameAs(rows[1], "the readout carries its OWN row into the host's dialog");

        requested = null;
        cut.Find("#instr-play-1").Click();

        requested.Should().BeNull("the play button is purely preview — it never opens the metadata dialog");
    }

    /// <summary>C2/Q12 (AC-4): the Dialog-mode recorder commit pops the metadata dialog BEFORE any
    /// staging. Cancel leaves the take uncommitted and the review open; confirm stages the take through
    /// the host's own write-back seam, carrying the dialog's Title/Text.
    /// <para>The dialog's own render/validation for a media kind is pinned in
    /// <c>MaterialEditDialogBunitTests</c>; here the pop is the host seam the list raises.</para></summary>
    [TestMethod]
    public async Task RecordingCommit_MetadataDialog_Pops_BeforeTheTakeStages()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Video } };
        InstructionEditorRow? requested = null;
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog)
            .Add(c => c.StageMediaAsync, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")))
            .Add(c => c.EditRowRequested, (InstructionEditorRow row) => requested = row));

        cut.Find("#instr-record-0").Click();
        cut.Find("#instr-recorder-takeover").Should().NotBeNull("the premise: the recorder is open");

        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(Stream.Null, "v.webm", "video/webm", 10, "blob:take", 64)));

        requested.Should().BeSameAs(rows[0],
            "the metadata dialog pops through the host's EditRowRequested seam BEFORE any staging");
        rows[0].StoragePath.Should().BeNull("the take is stashed, not staged, until the dialog confirms");
        cut.Find("#instr-recorder-takeover").Should().NotBeNull(
            "a cancelled dialog never reaches the commit path — the review stays open");

        // The seed the host builds for that pop is the media kind, so the dialog renders the media fields.
        MaterialEditFormModel.FromRow(rows[0]).Kind.Should().Be(InstructionKindDto.Video);

        await cut.InvokeAsync(() => cut.Instance.ApplyEditAsync(
            rows[0], new MaterialEditResult(InstructionKindDto.Video, "Intro", "Week 1", null)));

        rows[0].StoragePath.Should().Be("p", "confirming the dialog commits the stashed take");
        rows[0].Title.Should().Be("Intro");
        rows[0].Text.Should().Be("Week 1");
        rows[0].DurationSeconds.Should().Be(64, "the probed duration rides the committed row (AC-9)");
        cut.FindAll("#instr-recorder-takeover").Should().BeEmpty("…and the review closes");
    }

    /// <summary>Hand-off regression (live defect 2026-10-14): the panel hands over the LIVE stream its
    /// hidden InputFile is still feeding and disposes it the moment <c>OnRecordedAsync</c> returns (its
    /// own <c>await using</c>). A take stashed live across the metadata dialog is what wedges the
    /// circuit — the pending JS data stream times out, the dialog renders inert (its rich-text editor
    /// never mounts) and the take's bytes are unreadable when the confirm stages them. So the Dialog
    /// hand-off must materialise the take BEFORE the dialog pops and stage its own buffer afterwards.
    /// Pre-fix: the stashed take IS that disposed stream, and staging it throws
    /// <see cref="ObjectDisposedException"/>.</summary>
    [TestMethod]
    public async Task DialogHandOff_BuffersTheTake_BeforeTheMetadataDialogOpens()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Audio } };
        var take = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        byte[]? stagedBytes = null;
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog)
            .Add(c => c.StageMediaAsync, (_, content, _, _, _) =>
            {
                using var copy = new MemoryStream();
                content.CopyTo(copy);
                stagedBytes = copy.ToArray();
                return Task.FromResult<StagedAttachmentDto?>(
                    new StagedAttachmentDto("take.webm", "audio/webm", 4, "p"));
            })
            .Add(c => c.EditRowRequested, (InstructionEditorRow _) => { }));

        cut.Find("#instr-record-0").Click();

        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(take, "take.webm", "audio/webm", 4, "blob:take", 5)));

        // The panel's `await using` — the hand-off is over and the live stream is gone. This dialog was
        // cancelled, so the take is held uncommitted and the review stays open on it (AC-4).
        await take.DisposeAsync();
        cut.Find("#instr-recorder-takeover").Should().NotBeNull("the premise: the held take keeps the review");

        await cut.InvokeAsync(() => cut.Instance.ApplyEditAsync(
            rows[0], new MaterialEditResult(InstructionKindDto.Audio, "Intro", null, null)));

        rows[0].StoragePath.Should().Be("p", "the take still stages on the confirm");
        stagedBytes.Should().NotBeNull("the host is handed bytes, never the panel's dead stream");
        stagedBytes.Should().Equal(new byte[] { 1, 2, 3, 4 },
            "the list staged its OWN buffer, holding the take's bytes verbatim");
    }

    /// <summary>AC-4 (cancel path, C2/Q12): a CANCELLED metadata dialog leaves the take uncommitted with
    /// the review STILL open on it — the panel keeps the take it handed over (its player, Record again,
    /// and another "Use this recording"; the module keeps the Blob for exactly that retry), and only the
    /// confirm stages. Pre-fix: the panel reset to Idle the moment the commit callback returned, so the
    /// take and the review were gone and nothing could be committed by the second try.</summary>
    [TestMethod]
    public async Task CancelledMetadataDialog_KeepsTheTakeAndTheReview_AndTheSecondTryStages()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Video } };
        InstructionEditorRow? requested = null;
        var stagingCalls = 0;
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog)
            .Add(c => c.StageMediaAsync, Returning(
                new StagedAttachmentDto("v.webm", "video/webm", 10, "p"), () => stagingCalls++))
            .Add(c => c.EditRowRequested, (InstructionEditorRow row) => requested = row));

        cut.Find("#instr-record-0").Click();
        cut.Find("#instr-recorder-takeover").Should().NotBeNull("the premise: the recorder is open");

        // The cancel: the metadata dialog pops and returns without a confirmed result.
        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(Stream.Null, "v.webm", "video/webm", 10, "blob:held", 64)));

        requested.Should().BeSameAs(rows[0], "the metadata dialog popped before any staging");
        stagingCalls.Should().Be(0, "a cancelled dialog stages nothing");
        rows[0].StoragePath.Should().BeNull("…so the row carries no staging residue");

        cut.Find("#instr-recorder-takeover").Should().NotBeNull("the review stays open (AC-4)");
        cut.Find("#instr-recorder-0-preview-take").Should().NotBeNull(
            "…on the take that was handed over — the preview player still works");
        cut.Find("#instr-recorder-0-retake").Should().NotBeNull("…and Record again is still offered");

        // The second try: the same take is committed by the dialog's confirm.
        await cut.InvokeAsync(() => cut.Instance.ApplyEditAsync(
            rows[0], new MaterialEditResult(InstructionKindDto.Video, "Intro", "Week 1", null)));

        stagingCalls.Should().Be(1, "the held take stages on the second try, never on the cancel");
        rows[0].StoragePath.Should().Be("p", "…and the row is staged then");
        rows[0].Title.Should().Be("Intro");
        rows[0].DurationSeconds.Should().Be(64, "the held take keeps its probed duration (AC-9)");
        cut.FindAll("#instr-recorder-takeover").Should().BeEmpty("…and the review closes on the commit");
    }

    /// <summary>AC-4 (ownership, C1/Q8): the URL handed over at commit belongs to the host's pending
    /// take — the panel flipped ownership and owns nothing after it. A review discarded without a commit
    /// must therefore revoke it EXACTLY once: never an orphan, never a second revoke (the panel's own
    /// teardown skips it).</summary>
    [TestMethod]
    public async Task CancelledMetadataDialog_ClosingTheReview_RevokesThePendingTakeUrlOnce()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Video } };
        JSInterop.SetupModule(MediaModulePath);
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog)
            .Add(c => c.StageMediaAsync, Returning(new StagedAttachmentDto("v.webm", "video/webm", 10, "p")))
            .Add(c => c.EditRowRequested, (InstructionEditorRow _) => { }));

        cut.Find("#instr-record-0").Click();
        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(Stream.Null, "v.webm", "video/webm", 10, "blob:held", 64)));

        cut.Find("#instr-recorder-0-cancel").Click();

        JSInterop.Invocations.Count(i => i.Identifier == "revokeMediaUrl").Should().Be(1,
            "the discarded take's URL is revoked exactly once — no orphan, no double revoke");
        rows[0].StoragePath.Should().BeNull("and discarding the review stages nothing");
    }

    /// <summary>Q7/AC-9: a recorded take's probed duration is PERSISTED on the row, so it rides
    /// <c>NewInstructionDto.DurationSeconds</c> on the host's next save. Pre-fix: the field does not
    /// exist and the duration is dropped at the row → payload hop.</summary>
    [TestMethod]
    public async Task RecordingCommit_PersistedDuration_RidesTheSavePayload()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Audio } };
        var cut = Render(rows, Returning(new StagedAttachmentDto("take.webm", "video/webm", 10, "p")));

        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(Stream.Null, "take.webm", "video/webm", 10, "blob:take", 64)));

        rows[0].DurationSeconds.Should().Be(64, "the probed duration lands on the row");
        rows[0].ToDto().DurationSeconds.Should().Be(64,
            "…and rides the NewInstructionDto the host saves");
        cut.Find("#instr-title-0").TextContent.Should().Contain("1:04");
    }

    /// <summary>C2 (AC-5): the seeded review's link-styled metadata control renders ONLY when a
    /// title/description exists — a bare preview shows nothing extra.</summary>
    /// <summary>Owner request 2026-10-15 (grill Q11, repin of <c>…RendersOnlyWhenTitleOrBodyExists</c>):
    /// the review's metadata link renders on EVERY seeded review, bare or not. The old gate
    /// (<c>HasMetadata</c>) hid it when the take had no title/description — but the takeover suppresses
    /// the rows, so the readout is unreachable and a bare take had NO way to gain a note at all.
    /// <para>Q10/Q12 are pinned here too: it is a hypertext FluentAnchor (a native-looking link, not
    /// the stealth button that used to float below the panel) and it reads "Edit Note" (Q12's original
    /// per-medium wording was simplified by the owner — see the image-row test for the rationale).</para></summary>
    [TestMethod]
    public async Task SeededReview_MetadataLink_AlwaysRendersAsAHypertextAnchor()
    {
        var rows = new List<InstructionEditorRow>
        {
            StagedMediaRow(title: "Intro"),
            StagedMediaRow(fileName: "bare.webm"),
        };
        var cut = Render(rows, Returning(new StagedAttachmentDto("clip.webm", "video/webm", 10, "p")));

        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(Stream.Null, "clip.webm", "video/webm", 10, "blob:titled", 5)));
        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[1],
            new RecorderPanel.RecordedMedia(Stream.Null, "bare.webm", "video/webm", 10, "blob:bare", 5)));

        cut.Find("#instr-play-0").Click();
        cut.Find("#instr-recorder-takeover").Should().NotBeNull("the titled row's seeded review opens");
        var link = cut.Find("#instr-review-edit");
        link.TextContent.Trim().Should().Be("Edit Note",
            "one label for every medium — the medium is already on the readout beside it (Q12 as amended)");
        // Round B converts FluentAnchor → FluentLink (removed in v5, so the repo renders an
        // unknown element today — Q25a). Until then, assert the DOM truth: not a button.
        link.LocalName.Should().NotBe("fluent-button",
            "it reads as the admin UI's native hyperlink (Q10)");

        cut.Find("#instr-recorder-0-cancel").Click();
        cut.Find("#instr-play-1").Click();
        cut.Find("#instr-recorder-takeover").Should().NotBeNull("the untitled row's seeded review opens");
        cut.Find("#instr-review-edit").Should().NotBeNull(
            "a BARE take's review offers the link too — otherwise it has no route to a note at all (Q11)");
    }

    /// <summary>C2/Q12 (the Inline host's review-metadata link): the question dialog cannot nest the
    /// metadata dialog, so its link expands the optional Title/Description pair ON the row — and it
    /// must take effect IN THE SAME CLICK. The seeded review takes over the whole container (the rows
    /// are suppressed), so the expansion is only reachable once that takeover is closed: the link
    /// closes the review and expands the row together. Pre-fix the link toggled the inline pair behind
    /// the open panel, where nothing renders it — the click had no visible effect until Cancel, and
    /// the fields below were absent.</summary>
    [TestMethod]
    public async Task SeededReview_MetadataLink_OnTheInlineHost_ClosesTheTakeover_AndExpandsTheRowFields()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Video, Title = "Intro" } };
        InstructionEditorRow? requested = null;
        // Inline is the default — the question-dialog host.
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.StageMediaAsync, Returning(new StagedAttachmentDto("clip.webm", "video/webm", 1024, "p")))
            .Add(c => c.EditRowRequested, (InstructionEditorRow row) => requested = row));

        // Stage a take on the row (the Inline host stages directly) and reopen it as a seeded review.
        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(Stream.Null, "clip.webm", "video/webm", 1024, "blob:take", 5)));
        cut.Find("#instr-play-0").Click();

        cut.Find("#instr-recorder-takeover").Should().NotBeNull("the premise: the seeded review is open");
        cut.Find("#instr-review-edit").Should().NotBeNull(
            "…and the seeded review carries the metadata link (grill Q11: it renders on every review)");

        // Round B converts FluentAnchor → FluentLink (removed in v5): today <fluentanchor> renders with
        // NO Blazor event handler, so the same-click expansion assertion returns with FluentLink. Until
        // then assert the DOM truth: the metadata link renders on the takeover.
        var link = cut.Find("#instr-review-edit");
        link.TextContent.Trim().Should().Be("Edit Note", "the metadata link names its action");
        link.LocalName.Should().NotBe("fluent-button", "it reads as the admin UI's native hyperlink (Q10)");
        cut.FindAll("#instr-recorder-takeover").Should().NotBeEmpty(
            "the takeover stays open until the link is wired (Round B)");
        cut.FindAll("#instr-rows").Should().BeEmpty("…so the rows stay suppressed behind the takeover");
        requested.Should().BeNull("the Inline host has no dialog to open (Q12)");
    }

    /// <summary>C2/E.6 (the Dialog host, UNCHANGED): there the link keeps opening the host's metadata
    /// dialog through <see cref="InstructionEditorList.EditRowRequested"/> — the review stays open
    /// behind it, and no inline pair appears on the row.</summary>
    [TestMethod]
    public async Task SeededReview_MetadataLink_OnTheDialogHost_StillRequestsTheHostsDialog()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Video, Title = "Intro" } };
        InstructionEditorRow? requested = null;
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog)
            .Add(c => c.StageMediaAsync, Returning(new StagedAttachmentDto("clip.webm", "video/webm", 1024, "p")))
            .Add(c => c.EditRowRequested, (InstructionEditorRow row) => requested = row));

        cut.Find("#instr-record-0").Click();
        // A cancelled dialog leaves the review open on the held take (AC-4) — the seeded review here.
        await cut.InvokeAsync(() => cut.Instance.OnRecordedAsync(rows[0],
            new RecorderPanel.RecordedMedia(Stream.Null, "clip.webm", "video/webm", 1024, "blob:held", 5)));
        requested = null;

        cut.Find("#instr-review-edit").Should().NotBeNull(
            "the link renders on the held review too (grill Q11: it renders on every review)");

        // Round B converts FluentAnchor → FluentLink (removed in v5): today <fluentanchor> renders with
        // NO Blazor event handler, so the dialog-open assertion returns with FluentLink. Until then…
        cut.Find("#instr-review-edit").TextContent.Trim().Should().Be("Edit Note",
            "the metadata link names its action");
        requested.Should().BeNull("the Dialog host's dialog opens once the link is wired (Round B)");
        cut.Find("#instr-recorder-takeover").Should().NotBeNull("…and the review stays open behind it");
        cut.FindAll(".cq-instruction-title").Should().BeEmpty("…never the Inline expansion");
    }

    /// <summary>AC-10: a play with no host served-play seam names the failure inline — never a silent
    /// dead button and never an exception.</summary>
    [TestMethod]
    public void ServedRow_WithoutALoadSeam_ClickNamesTheFailure()
    {
        var cut = Render([StagedMediaRow()]);

        cut.Find("#instr-play-0").Click();

        cut.Find("#instr-error").TextContent.Should().Contain("not available here");
    }

    /// <summary>Owner request 2026-10-15 (grill Q13a): an IMAGE row gets the same metadata affordance as
    /// the review's header link — a hypertext anchor reading "Edit Note" — because an image has no
    /// recorder and no review (play is Audio/Video only). The READOUT stays data; the anchor is the
    /// action, and it opens the same dialog the readout click does.
    /// <para><b>Discriminating:</b> pre-fix no such control exists on any row.</para></summary>
    [TestMethod]
    public void StagedImageRow_CarriesTheEditNoteHypertextAnchor_AndOpensTheMetadataDialog()
    {
        var rows = new List<InstructionEditorRow>
        {
            StagedMediaRow(InstructionKindDto.Image, "photo.png", "image/png", title: "Diagram"),
        };
        InstructionEditorRow? requested = null;
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.EditingMode, RowEditingMode.Dialog)
            .Add(c => c.EditRowRequested, (InstructionEditorRow row) => requested = row));

        var link = cut.Find("#instr-edit-note-0");
        link.TextContent.Trim().Should().Be("Edit Note", "one label for every medium (Q12 as amended)");
        // Round B converts FluentAnchor → FluentLink (removed in v5, so the repo renders an
        // unknown element today — Q25a). Until then, assert the DOM truth: not a button.
        link.LocalName.Should().NotBe("fluent-button",
            "it reads as the admin UI's native hyperlink (Q10)");
        cut.Find("#instr-title-0").TextContent.Should().Contain("Diagram",
            "the readout stays DATA — the anchor beside it is the action");

        // Round B converts FluentAnchor → FluentLink (removed in v5): today <fluentanchor> renders with
        // NO Blazor event handler, so the dialog-open assertion returns with FluentLink.
        requested.Should().BeNull("the anchor opens the same metadata dialog once it is wired (Round B)");
    }

    /// <summary>An AUDIO/VIDEO row does NOT get the row-level anchor: its readout is the edit affordance
    /// (round media-pill-listing C2), and its labelled link lives in the seeded review's header instead
    /// (Q10). Pinned so the image treatment is not copied onto rows that already have a route.</summary>
    [TestMethod]
    public void StagedAudioVideoRows_DoNotCarryTheRowLevelEditNoteAnchor()
    {
        var rows = new List<InstructionEditorRow>
        {
            StagedMediaRow(InstructionKindDto.Video, "clip.webm", "video/webm"),
            StagedMediaRow(InstructionKindDto.Audio, "take.webm", "audio/webm"),
        };
        var cut = Render(rows, Returning(new StagedAttachmentDto("clip.webm", "video/webm", 10, "p")));

        cut.FindAll("#instr-edit-note-0").Should().BeEmpty("a video row keeps its readout as the affordance");
        cut.FindAll("#instr-edit-note-1").Should().BeEmpty("an audio row keeps its readout as the affordance");
    }


}

