using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeacherGradeAssignments;

/// <summary>
/// The grade-scoped teaching assignments for a teacher (v4 spec §3.5): each row
/// is a <see cref="SchoolCollab.Students.Core.Domain.TeacherGradeLevel"/> =
/// grade + optional subject + role.
/// </summary>
public sealed record ListTeacherGradeAssignments(Guid TeacherId) : IQuery<SchoolCollab.Students.Core.DTOs.TeacherGradeAssignmentDto[]>;
