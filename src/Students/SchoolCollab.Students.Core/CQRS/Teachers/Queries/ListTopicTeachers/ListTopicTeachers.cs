using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTopicTeachers;

/// <summary>
/// Teachers linked to a topic, each carrying their coded-value role on that topic
/// (grade-detail-rich-grids-plan.md §5). Tenant-scoped. Used by the grade Detail
/// topic-teachers dialog.
/// </summary>
public sealed record ListTopicTeachers(Guid TopicId) : IQuery<SchoolCollab.Students.Core.DTOs.TopicTeacherDto[]>;
