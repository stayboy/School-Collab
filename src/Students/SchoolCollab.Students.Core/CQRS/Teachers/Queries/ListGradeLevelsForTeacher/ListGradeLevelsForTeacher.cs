using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListGradeLevelsForTeacher;

/// <summary>Grade levels a teacher teaches (spec §4.12).</summary>
public sealed record ListGradeLevelsForTeacher(Guid TeacherId) : IQuery<GradeLevelDto[]>;
