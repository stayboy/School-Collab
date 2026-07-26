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
    Guid? TitleCodedValueId = null,
    ContactChannel? PrimaryContactChannel = null,
    string? PrimaryContactValue = null,
    string? PrimaryContactCountryCode = null);
