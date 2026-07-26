namespace SchoolCollab.Students.Core.DTOs;

using SchoolCollab.Students.Core.Domain;

/// <summary>
/// Tenant guardian list / detail DTO. Carries the guardian's identity +
/// profile fields, plus (optionally) the guardian's primary contact
/// (channel / value / country code) so list UIs can show how to reach the
/// guardian without a second round-trip per row. The primary-contact
/// fields are null when the guardian has no contacts. Relationships are
/// NOT included here — a relationship is per student-guardian link, not a
/// guardian property (see <see cref="StudentGuardianViewDto"/>).
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
    DateTimeOffset UpdatedAt,
    ContactChannel? PrimaryContactChannel = null,
    string? PrimaryContactValue = null,
    string? PrimaryContactCountryCode = null);
