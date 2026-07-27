namespace SchoolCollab.Students.Admin.Components.Students;

/// <summary>Trivial form model for <see cref="GuardianPickerDialog"/> — a
/// selection list, not a form. Exists only to satisfy the
/// <c>DialogShellBase</c> contract.</summary>
/// <param name="excludeStudentId">When set, the picker asks the backend
/// to exclude guardians already linked to this student (server-side
/// filter via <c>ListGuardians.ExcludeStudentId</c>) so the user cannot
/// double-link the same guardian. Null = no exclusion (offer every tenant
/// guardian). Used by the student view/edit pages; the wizard passes the
/// student id when the student is already persisted (otherwise null — no
/// links exist yet, so nothing to exclude).</param>
public sealed record GuardianPickerModel(Guid? ExcludeStudentId = null);
