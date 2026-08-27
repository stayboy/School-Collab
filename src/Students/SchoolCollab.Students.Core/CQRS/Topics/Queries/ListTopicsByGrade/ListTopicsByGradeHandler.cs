using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicsByGrade;

/// <summary>
/// Returns the shared topics assigned to a grade level on an effective date.
///
/// <para>Topics are <b>shared, global</b> catalog definitions; a topic is
/// linked to a grade via the <see cref="Domain.GradeTopicAssignment"/> bridge
/// (subject-to-topic-polymorphism.md §2.4). The bridge is <b>date-based, not
/// period-bound</b>: a topic is in effect on a date when
/// <c>StartDate &lt;= date &lt;= (EndDate ?? ∞)</c>. Blocked/archived assignments
/// have an <c>EndDate</c> and are therefore excluded from the effective set.</para>
///
/// <para>When <c>effectiveDate</c> is omitted, today is used. The Topics landing
/// page shows exactly the grade's currently-effective topics.</para>
/// </summary>
public sealed class ListTopicsByGradeHandler(StudentsDbContext db)
    : IQueryHandler<ListTopicsByGrade, TopicDto[]>
{
    public async Task<TopicDto[]> HandleAsync(
        ListTopicsByGrade query,
        CancellationToken cancellationToken = default)
    {
        var effectiveDate = query.EffectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // When a PeriodId is supplied, restrict to assignments scoped to that
        // period (Rev. 6 FR-55). Otherwise fall back to the date-based effective
        // window (year-spanning + period-aligned assignments in effect on the date).
        var topicIds = db.GradeTopicAssignments
            .AsNoTracking()
            .Where(a => a.GradeLevelId == query.GradeLevelId
                && (query.PeriodId == null || a.PeriodId == query.PeriodId)
                && a.StartDate <= effectiveDate
                && (a.EndDate == null || a.EndDate >= effectiveDate));

        var ids = await topicIds
            .Select(a => a.TopicId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var subjects = await db.Topics
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .OrderBy(t => t.DisplayOrder)
            .ThenBy(t => t.Name)
            .ToArrayAsync(cancellationToken);

        return subjects
            .Select(s => new TopicDto(
                s.Id,
                s.CodedValueId,
                s.Code,
                s.Name,
                s.Description,
                s.DisplayOrder,
                s.CreatedAt,
                s.UpdatedAt))
            .ToArray();
    }
}
