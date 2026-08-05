using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.CreateGradeLevel;

public sealed record CreateGradeLevel(
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder,
    int? MinAge = null,
    int? MaxAge = null,
    Guid? AllowedGenderCodedValueId = null,
    bool IsBlockedFromEnrollment = false) : ICommand;