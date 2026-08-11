namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// A grade-scoped teaching assignment row (v4 spec §3.5). Backed by
/// <see cref="SchoolCollab.Students.Core.Domain.TeacherGradeLevel"/>: a row is a
/// grade + optional subject + role. Returned by <c>ListTeacherGradeAssignments</c>.
/// </summary>
public sealed record TeacherGradeAssignmentDto(
    Guid RowId,
    Guid GradeLevelId,
    string GradeName,
    int GradeLevel,
    Guid? SubjectId,
    string? SubjectName,
    string? SubjectCode,
    Guid? RoleCodedValueId);
