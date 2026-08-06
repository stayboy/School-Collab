namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Per-topic curriculum counts for a grade-level's assigned topics, used to pad
/// the Topics &amp; Curriculum grid (grade-detail-rich-grids-plan.md). Strands
/// and lessons are topic-scoped, so the counts are the topic's totals.
/// </summary>
public sealed record GradeTopicCurriculumDto(
    Guid TopicId,
    string Name,
    string? Code,
    int StrandCount,
    int LessonCount);
