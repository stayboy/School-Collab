using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacherGradeAssignment;

/// <summary>
/// Removes one grade-scoped assignment row (a <see cref="SchoolCollab.Students.Core.Domain.TeacherGradeLevel"/>)
/// for a teacher (v4 spec §3.5).
/// </summary>
public sealed record DeleteTeacherGradeAssignment(Guid TeacherId, Guid RowId) : ICommand;
