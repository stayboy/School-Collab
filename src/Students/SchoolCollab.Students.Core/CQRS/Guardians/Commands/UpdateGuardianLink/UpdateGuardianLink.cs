using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.UpdateGuardianLink;

public sealed record UpdateGuardianLink(
    Guid StudentId,
    Guid GuardianId,
    GuardianRole Role,
    Guid? RelationshipCodedValueId,
    bool IsEmergencyContact) : ICommand;
