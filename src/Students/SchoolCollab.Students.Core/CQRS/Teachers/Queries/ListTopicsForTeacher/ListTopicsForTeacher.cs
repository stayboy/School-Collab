using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTopicsForTeacher;

/// <summary>Topics a teacher teaches (spec §4.12).</summary>
public sealed record ListTopicsForTeacher(Guid TeacherId) : IQuery<TopicDto[]>;
