using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// WS-C1 (Q5 delegation) — form state for the reassign-signer dialog.
/// The dialog is dumb by design: the hosting SignOffSection already fetched
/// the ward's guardians (via the sign-off context aggregate) and passes them
/// here; the teacher picks the new expected signer; the dialog returns the
/// picked guardian id.
/// </summary>
public sealed class ReassignSignerFormModel(string studentName, IReadOnlyList<WardGuardianDto> guardians)
{
    /// <summary>The ward's display name (for the dialog hint).</summary>
    public string StudentName { get; } = studentName;

    /// <summary>The linked guardian options (primary-priority enforcement is
    /// recorded backlog — any linked guardian may be assigned in v1).</summary>
    public IReadOnlyList<WardGuardianDto> Guardians { get; } = guardians;

    /// <summary>The picked new expected signer (null until the teacher selects).</summary>
    public Guid? SelectedGuardianId { get; set; }
}

/// <summary>
/// WS-C1 — result returned when the teacher confirms a reassignment: the picked
/// new expected signer's guardian id. A class wrapper (not a nullable struct)
/// because <see cref="SchoolCollab.Admin.Shared.Components.Dialogs.DialogShellBase{TModel,TResult}"/>
/// constrains <c>TResult : class</c>.
/// </summary>
public sealed record ReassignSignerResult(Guid GuardianId);