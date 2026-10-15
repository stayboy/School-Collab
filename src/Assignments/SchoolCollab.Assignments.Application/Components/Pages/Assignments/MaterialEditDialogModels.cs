using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>The dialog's form state (Q1: ONE dialog, its fields switching on <see cref="Kind"/>) —
/// the shape the DialogShellBase contract wants: a mutable <c>class</c>.</summary>
public sealed class MaterialEditFormModel
{
    /// <summary>Text, Link or a media kind (Audio/Video/Image). The dialog's field switch renders one
    /// shape per kind: Text/Link edit their target/label as before, and a media kind edits the optional
    /// Title + Description (round <c>media-pill-listing</c> C2 — the staged FILE never comes through
    /// here; only its metadata does).</summary>
    public InstructionKindDto Kind { get; set; } = InstructionKindDto.Text;

    /// <summary>The Text material's NAME (required — D6/D7), the Link's optional LABEL and a media
    /// kind's optional TITLE — one field per kind so the dialog can render one input and the handler
    /// can read one value.</summary>
    public string? Title { get; set; }

    /// <summary>The Text material's optional body, or a media kind's optional plain-prose description
    /// (round <c>media-pill-listing</c> C2 — both optional and independent).</summary>
    public string? Body { get; set; }

    /// <summary>Post-close rework (owner 2026-10-14): the media kinds' preview seam — resolves the
    /// row's preview Blob URL LAZILY on the first toggle press (a pending take or session entry
    /// returns its kept URL instantly; a persisted row streams its bytes through the host's served
    /// route on demand, so opening the dialog never blocks on a large file). The seam caches
    /// list-side, and the dialog never revokes. Null = no preview can resolve here — the dialog then
    /// renders no toggle at all, never a dead button.</summary>
    public Func<Task<string?>>? PreviewLoader { get; set; }

    /// <summary>The Link's target — required, absolute http(s) (mirrors the validator).</summary>
    public string? Url { get; set; }

    /// <summary>True when this dialog was opened on an EXISTING row rather than to create one
    /// (round <c>material-row-editing</c>): only the footer's verbs change — "Save" instead of
    /// "Add Text Content"/"Add Link". The fields, the validator mirror and the payload are the same
    /// shape on both paths, which is the whole point of Q4(b): one dialog, create and edit.</summary>
    public bool IsEdit { get; set; }

    /// <summary>Seeds the form from a row being edited (AC-14: the row's values ARE what the dialog
    /// opens with). The Text row's body travels as <see cref="Body"/> and its prose as
    /// <see cref="InstructionEditorRow.Text"/> — the one rename this mapping exists to hold.</summary>
    public static MaterialEditFormModel FromRow(InstructionEditorRow row) => new()
    {
        Kind = row.Kind,
        Title = row.Title,
        Body = row.Text,
        Url = row.Url,
        IsEdit = true,
    };
}

/// <summary>What the dialog hands back when it is confirmed. The page appends the row from this — the
/// dialog never touches the list (Q2: the list stays the single writer of its rows).</summary>
public sealed record MaterialEditResult(
    InstructionKindDto Kind,
    string? Title,
    string? Body,
    string? Url);
