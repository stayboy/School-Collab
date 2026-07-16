namespace SchoolCollab.Students.Admin.Components.Students;

/// <summary>Trivial form model for <see cref="StudentPickerDialog"/> — a
/// selection list, not a form. Exists only to satisfy the
/// <c>DialogShellBase</c> contract.
/// <para><see cref="ExcludedStudentIds"/> lets the caller (e.g. the grade-level
/// wizard) scope the picker to students not already assigned to the current
/// grade or to any other grade for the period — transfers are handled
/// elsewhere, not in the picker.</para></summary>
public sealed record StudentPickerModel(
    IEnumerable<Guid>? ExcludedStudentIds = null,
    Guid? PeriodId = null);
