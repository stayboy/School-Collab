namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Per-topic curriculum counts for a grade-level's assigned topics, used to pad
/// the Topics &amp; Curriculum grid (grade-detail-rich-grids-plan.md). Strands
/// and lessons are topic-scoped, so the counts are the topic's totals.
/// </summary>
/// <param name="AssignmentId">The bridge row (grade ↔ topic assignment) id. The
/// reorder endpoint targets the ASSIGNMENT, not the topic: one topic may be
/// assigned to several grades, so only the bridge row identifies this grade's
/// entry.</param>
public sealed record GradeTopicCurriculumDto(
    Guid AssignmentId,
    Guid TopicId,
    string Name,
    string? Code,
    int StrandCount,
    int LessonCount);
