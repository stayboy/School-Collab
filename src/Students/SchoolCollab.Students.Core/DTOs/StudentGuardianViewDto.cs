using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// UI-facing projection returned by <c>GET /students/{studentId}/guardians</c>
/// (spec §9 / §11). Carries both the <see cref="StudentGuardian"/> link data
/// (role, relationship, emergency-contact flag) and the guardian's display
/// name, so the student Guardians tab can render "grouped by role" without a
/// second round-trip per guardian.
/// </summary>
public sealed record StudentGuardianViewDto(
    Guid GuardianId,
    Guid StudentId,
    GuardianRole Role,
    Guid? RelationshipCodedValueId,
    bool IsEmergencyContact,
    string FirstName,
    string LastName,
    string? DisplayName,
    Guid? TitleCodedValueId = null)
{
    /// <summary>
    /// The guardian's top contacts in display order (index 0 is the
    /// preferred contact). List/grid UI can render up to the first three
    /// entries. Empty when the guardian has no contacts.
    /// </summary>
    public IReadOnlyList<GuardianContactViewDto> Contacts { get; init; } =
        System.Array.Empty<GuardianContactViewDto>();

    /// <summary>
    /// Total number of non-deleted contacts for this guardian (NOT capped at
    /// 3). Used by the student-view guardians grid to decide whether to show
    /// the "View all (N) contacts" anchor beneath the name — the anchor is
    /// shown only when <see cref="HasMoreContacts"/> is true (i.e. more than
    /// 3). <see cref="Contacts"/> carries only the top 3, so
    /// <c>Contacts.Count == 3</c> is ambiguous between exactly-3 and
    /// more-than-3; this count is the authoritative "are there more?" signal.
    /// Defaults to 0 for callers that do not set it (e.g. the picker list
    /// handler, which never renders the anchor).
    /// </summary>
    public int TotalContactCount { get; init; }

    /// <summary>True when the guardian has more than the 3 contacts shown
    /// inline in the grid. Convenience over <see cref="TotalContactCount"/>.
    /// </summary>
    public bool HasMoreContacts => TotalContactCount > 3;
}
