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
        List<InstructionEditorRow> rows, StageInstructionMediaAsync? stage = null) =>
        Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.StageMediaAsync, stage));

    private static StageInstructionMediaAsync Returning(StagedAttachmentDto? staged, Action? onCall = null) =>
        (row, content, name, type, size) =>
        {
            onCall?.Invoke();
            return Task.FromResult(staged);
        };

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

    /// <summary>Owner pill rework (2026-10-12): a staged media item WITHOUT a session take (uploaded, or
    /// reloaded later) reads as kind icon + size text — no raw-filename dump, and the clear-file × is
    /// retired; the row's trailing remove × is the single close.</summary>
    [TestMethod]
    public void StagedMedia_RendersTheSizeText_WithNoClearAction()
    {
        var cut = Render(
        [
            new InstructionEditorRow
            {
                Kind = InstructionKindDto.Audio,
                FileName = "how-to.mp3",
                ContentType = "audio/mpeg",
                FileSize = 2048,
                StoragePath = "tenants/t/staging/g/how-to.mp3",
            },
        ]);

        cut.Markup.Should().Contain(AttachmentUploadPolicy.FormatFileSize(2048),
            "the size reads instead of the raw filename");
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
        cut.FindAll("fluent-text-field").Should().BeEmpty(
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
        cut.FindAll("fluent-text-field").Should().NotBeEmpty("…it still edits inline");
    }

    /// <summary>Grill Q3: media rows are staged, not formed — the file IS the value, so Dialog mode
    /// leaves them on the staged-file flow (owner pill rework, 2026-10-12: the flow now reads as the
    /// compact size-text pill; the clear-file × is retired and the row's remove × is the single close).
    ///</summary>
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

        cut.FindAll("#instr-edit-0").Should().BeEmpty("a staged file is not seeded into a form");
        cut.FindAll(".cq-instruction-file").Should().ContainSingle(
            "the staged file still reads in the row (size text, title tooltip)");
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

    /// <summary>Owner pill rework (2026-10-12): a committed take's session Blob URL + duration ride the
    /// commit seam into the row's play pill — play + "m:ss" — and the pill reopens the SAME review
    /// panel (player + Record again, no commit button). Closing keeps the entry; the row survives.</summary>
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
        cut.Find("#instr-play-0").TextContent.Should().Contain("1:04",
            "the pill carries the take's probed duration");
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

}

