using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>The dialog's form state (Q1: ONE dialog, its fields switching on <see cref="Kind"/>) —
/// the shape the DialogShellBase contract wants: a mutable <c>class</c>.</summary>
public sealed class MaterialEditFormModel
{
    /// <summary>Text or Link — the two kinds this dialog captures. Media never comes through here:
    /// an upload pops the picker directly (Q3).</summary>
    public InstructionKindDto Kind { get; set; } = InstructionKindDto.Text;

    /// <summary>The Text material's NAME (required — D6/D7) and the Link's optional LABEL, one field
    /// per kind so the dialog can render one input and the handler can read one value.</summary>
    public string? Title { get; set; }

    /// <summary>The Text material's optional body.</summary>
    public string? Body { get; set; }

    /// <summary>The Link's target — required, absolute http(s) (mirrors the validator).</summary>
    public string? Url { get; set; }
}

/// <summary>What the dialog hands back when it is confirmed. The page appends the row from this — the
/// dialog never touches the list (Q2: the list stays the single writer of its rows).</summary>
public sealed record MaterialEditResult(
    InstructionKindDto Kind,
    string? Title,
    string? Body,
    string? Url);
