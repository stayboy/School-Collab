using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherActivityAssignment;

/// <summary>
/// Links a teacher to an activity group with an optional role and optional grades
/// (v4 spec §3.5). Upsert semantics are handled at the repository layer.
/// </summary>
public sealed record LinkTeacherActivityAssignment(
    Guid TeacherId,
    Guid ActivityGroupId,
    Guid? RoleCodedValueId = null,
    Guid[]? GradeLevelIds = null) : ICommand;
