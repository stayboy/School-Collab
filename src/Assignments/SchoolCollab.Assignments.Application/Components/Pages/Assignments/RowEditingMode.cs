namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// How a Text/Link row is edited (round <c>material-row-editing</c>, the seam its plan forces).
///
/// <para>ONE component serves two hosts, and they cannot agree on the interaction: the authoring
/// page can open <see cref="MaterialEditDialog"/>, while <c>QuestionEditDialog</c> is itself a dialog
/// and the repo forbids nesting them — so the question's own instruction list must keep editing in
/// the row. Naming the two modes turns that from accidental divergence into a decision: every host
/// states its mode, and a reader sees immediately why the two screens differ.</para>
/// </summary>
public enum RowEditingMode
{
    /// <summary>The row renders its own fields (the pre-redesign shape). The default, so the
    /// question dialog's host — which cannot open a dialog — is correct without opting in.</summary>
    Inline,

    /// <summary>The row READS OUT its values and offers an edit affordance that raises
    /// <c>InstructionEditorList.EditRowRequested</c>; the host opens the dialog and applies the
    /// result back. The authoring page's mode (Q4(b) of the redesign round-up grill).</summary>
    Dialog,
}
