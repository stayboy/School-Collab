namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// The kind of mutation recorded in a <see cref="FlagAuditEntry"/>. Append-only;
/// values are never removed.
/// </summary>
public enum FlagChangeKind
{
    Created = 0,
    Renamed = 1,
    DescriptionChanged = 2,
    Enabled = 3,
    Disabled = 4,
    Archived = 5,
    Unarchived = 6,
    Deleted = 7,
    Recovered = 8,
    OverrideCreated = 9,
    OverrideUpdated = 10,
    OverrideDeleted = 11,
}