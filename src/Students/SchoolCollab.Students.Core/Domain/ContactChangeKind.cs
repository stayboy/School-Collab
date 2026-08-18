namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// The kind of mutation recorded in a <see cref="ContactAuditEntry"/>. Append-only;
/// values are never removed.
/// </summary>
public enum ContactChangeKind
{
    Updated = 0,
    Deleted = 1,
}
