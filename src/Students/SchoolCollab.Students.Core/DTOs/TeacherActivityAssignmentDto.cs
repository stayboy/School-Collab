namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// A teacher↔activity assignment row (v4 spec §3.5): activity + role + optional
/// grades. Returned by <c>ListTeacherActivityAssignments</c>.
/// </summary>
public sealed record TeacherActivityAssignmentDto(
    Guid RowId,
    Guid ActivityGroupId,
    string ActivityName,
    Guid? RoleCodedValueId,
    Guid[] GradeLevelIds);
