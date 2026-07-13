using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListSubjectsForTeacher;

/// <summary>Subjects a teacher teaches (spec §4.12).</summary>
public sealed record ListSubjectsForTeacher(Guid TeacherId) : IQuery<SubjectDto[]>;
