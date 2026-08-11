using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeacherActivityAssignments;

/// <summary>
/// The teacher↔activity assignments for a teacher (v4 spec §3.5): activity +
/// role + optional grades.
/// </summary>
public sealed record ListTeacherActivityAssignments(Guid TeacherId) : IQuery<SchoolCollab.Students.Core.DTOs.TeacherActivityAssignmentDto[]>;
