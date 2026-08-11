using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacherActivityAssignment;

/// <summary>
/// Removes one teacher↔activity assignment row (and its grades) (v4 spec §3.5).
/// </summary>
public sealed record DeleteTeacherActivityAssignment(Guid TeacherId, Guid RowId) : ICommand;
