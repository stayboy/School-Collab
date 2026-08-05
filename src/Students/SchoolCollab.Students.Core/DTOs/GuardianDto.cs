namespace SchoolCollab.Students.Core.DTOs;

using SchoolCollab.Students.Core.Domain;

/// <summary>
/// Tenant guardian list / detail DTO. Carries the guardian's identity +
/// profile fields, plus the guardian's top contacts (<see cref="Contacts"/>)
/// so list UIs can show how to reach the guardian without a second
/// round-trip per row. Relationships are NOT included here — a
/// relationship is per student-guardian link, not a guardian property
/// (see <see cref="StudentGuardianViewDto"/>).
/// </summary>
public sealed record GuardianDto(
    Guid Id,
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    string? Address,
    Guid? CommunityId,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// The guardian's top contacts in display order (index 0 is the
    /// preferred contact). List/grid UI can render up to the first three
    /// entries. Empty when the guardian has no contacts.
    /// </summary>
    public IReadOnlyList<GuardianContactViewDto> Contacts { get; init; } =
        System.Array.Empty<GuardianContactViewDto>();

    /// <summary>
    /// Number of linked (non-deleted) students for this guardian. Populated
    /// client-side (like <c>Contacts</c> is populated by the handler) from the
    /// bulk <c>GET /guardians/student-counts</c> endpoint so the guardians
    /// landing page can render an "N students" cell without an N+1 fetch.
    /// Left null when unknown (e.g. a single-record fetch or enrichment
    /// failure) — the landing page renders "—" for null vs "0 students" for
    /// an explicit zero.
    /// </summary>
    public int? StudentCount { get; init; }

    /// <summary>
    /// Display name of the guardian's salutation title (e.g. "Mr", "Mrs"),
    /// resolved client-side from <see cref="TitleCodedValueId"/> so the
    /// landing page can render the same combined "title + name" format as
    /// <c>GuardianGrid.FormatGuardianName</c>. Null when the guardian has no
    /// title or the title could not be resolved (the cell falls back to
    /// DisplayName-or-FirstLast).
    /// </summary>
    public string? TitleName { get; init; }

    /// <summary>
    /// Relationship coded value id on the student↔guardian link for the student
    /// this list is scoped to (via <c>?studentId=</c>). Null for the unfiltered
    /// tenant-level list — a relationship is per student-guardian link, not a
    /// guardian property (see <see cref="StudentGuardianViewDto"/>), so it is
    /// only meaningful when the list is scoped to one student. Client-enriched
    /// into <see cref="RelationshipName"/>.
    /// </summary>
    public Guid? RelationshipCodedValueId { get; init; }

    /// <summary>
    /// Resolved display name of <see cref="RelationshipCodedValueId"/>
    /// (e.g. "Mother", "Father"), enriched client-side so the student-scoped
    /// guardians landing page can render "name (relationship)". Null when not
    /// scoped to a student or the coded value could not be resolved (the cell
    /// falls back to title + name with no relationship).
    /// </summary>
    public string? RelationshipName { get; init; }
}
