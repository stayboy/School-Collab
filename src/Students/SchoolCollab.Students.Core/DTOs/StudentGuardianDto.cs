using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.DTOs;

public sealed record StudentGuardianDto(
    Guid Id,
    Guid StudentId,
    Guid GuardianId,
    Guid? RelationshipCodedValueId,
    GuardianRole Role,
    bool IsEmergencyContact,
    Guid? CreatedByGuardianId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
