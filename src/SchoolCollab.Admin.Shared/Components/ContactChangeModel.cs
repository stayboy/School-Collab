using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Dialog mode for <see cref="ContactChangeDialog"/>: edit an existing contact
/// or soft-delete it after collecting a reason.
/// </summary>
public enum ContactChangeMode { Edit, Delete }

/// <summary>
/// Form-state model passed to <see cref="ContactChangeDialog"/>. Carries the
/// contact's current snapshot and the requested mode (edit or delete).
/// </summary>
public sealed record ContactChangeModel(
    Guid ContactId,
    ContactChannel Channel,
    string Value,
    string? Label,
    string? CountryCode,
    ContactChangeMode Mode);

/// <summary>
/// Result returned by <see cref="ContactChangeDialog"/> when the user confirms.
/// For edits, all new fields are populated. For deletes, <see cref="IsDeleted"/>
/// is true and the value fields are null (the reason is still required).
/// </summary>
public sealed record ContactChangeResult(
    ContactChannel? Channel,
    string? Value,
    string? Label,
    string? CountryCode,
    string Reason,
    bool IsDeleted);
