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
}
