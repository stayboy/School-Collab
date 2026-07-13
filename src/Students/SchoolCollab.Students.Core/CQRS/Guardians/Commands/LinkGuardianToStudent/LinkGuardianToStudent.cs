using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.LinkGuardianToStudent;

/// <summary>
/// Links a guardian to a student with a Primary/CC role. <see cref="ActingGuardianId"/>
/// records a portal-created CC link (set by a Primary guardian; null for teacher/admin).
/// </summary>
public sealed record LinkGuardianToStudent(
    Guid StudentId,
    Guid GuardianId,
    Guid? RelationshipCodedValueId,
    GuardianRole Role,
    bool IsEmergencyContact,
    Guid? ActingGuardianId) : ICommand;
