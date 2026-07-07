using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.GetOrCreateGradeLevel;

/// <summary>
/// Find-or-create a <see cref="Domain.GradeLevel"/> by <see cref="CodedValueId"/>
/// (safe since the unique index from §5.7 guarantees at most one grade level per
/// coded value). If a grade level for the coded value exists, it is reused and its
/// mirrored <see cref="Domain.GradeLevel.Name"/>/Level/DisplayOrder are updated;
/// otherwise a new one is created. Used by the wizard's save step (§6.3). Returns
/// the resulting grade level as a <see cref="GradeLevelDto"/>.
/// </summary>
public sealed record GetOrCreateGradeLevel(
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder) : ICommand;