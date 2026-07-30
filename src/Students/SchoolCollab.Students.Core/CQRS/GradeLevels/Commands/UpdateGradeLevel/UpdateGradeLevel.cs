using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.UpdateGradeLevel;

public sealed record UpdateGradeLevel(
    Guid Id,
    int Level,
    string Name,
    int DisplayOrder,
    int? MinAge = null,
    int? MaxAge = null,
    Guid? AllowedGenderCodedValueId = null) : ICommand;