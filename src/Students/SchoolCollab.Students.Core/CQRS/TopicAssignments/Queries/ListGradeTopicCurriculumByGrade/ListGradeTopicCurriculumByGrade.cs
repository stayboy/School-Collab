using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicCurriculumByGrade;

/// <summary>
/// Per-topic strand/lesson counts for a grade level's currently-assigned topics
/// (grade-detail-rich-grids-plan.md §4). Strands and lessons are topic-scoped, so
/// the counts are the topic's totals regardless of grade.
/// </summary>
public sealed record ListGradeTopicCurriculumByGrade(Guid GradeLevelId, DateOnly EffectiveDate) : IQuery<GradeTopicCurriculumDto[]>;
