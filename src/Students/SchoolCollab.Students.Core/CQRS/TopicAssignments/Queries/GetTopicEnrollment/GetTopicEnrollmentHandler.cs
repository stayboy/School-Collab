using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.GetTopicEnrollment;

/// <summary>
/// The publish filter's eligibility answer (FR-ER-14). See
/// <see cref="TopicEnrollmentDto"/> for why the response is two id arrays rather than one.
/// <para><b>The flag is read from the grade↔topic bridge row effective today</b> — the same
/// <c>StartDate &lt;= today &amp;&amp; (EndDate ?? today) &gt;= today</c> filter every existing
/// reader applies. No effective bridge row means there is no subject to be enrolled in, so
/// both arrays are empty (never the whole audience: guessing <c>false</c> would over-send).</para>
/// <para><b>No active academic year does not error.</b> Nothing can be enrolled without a
/// year (rows are year-keyed, FR-ER-3), so <see cref="TopicEnrollmentDto.Eligible"/> is empty
/// while <see cref="TopicEnrollmentDto.Audience"/> stays the grade roster — a <c>true</c>
/// subject therefore excludes the whole grade. Both arrays empty would be a different, wrong
/// answer: the resolver would compute an empty exclusion set and over-send.</para>
/// </summary>
public sealed class GetTopicEnrollmentHandler(
    StudentsDbContext db,
    IActivePeriodProvider activePeriodProvider) : IQueryHandler<GetTopicEnrollment, TopicEnrollmentDto>
{
    public async Task<TopicEnrollmentDto> HandleAsync(
        GetTopicEnrollment query,
        CancellationToken cancellationToken = default)
    {
        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var requiresEnrollment = await db.GradeTopicAssignments
            .AsNoTracking()
            .Where(x => x.GradeLevelId == query.GradeLevelId
                        && x.TopicId == query.TopicId
                        && x.StartDate <= effectiveDate
                        && (x.EndDate == null || x.EndDate >= effectiveDate))
            .Select(x => (bool?)x.RequiresEnrollment)
            .FirstOrDefaultAsync(cancellationToken);

        if (requiresEnrollment is null) return new TopicEnrollmentDto([], []);

        var activeYear = await activePeriodProvider.GetActiveAcademicYearAsync(cancellationToken);
        var audience = await db.ActiveGradeRosterAsync(query.GradeLevelId, activeYear?.Id, cancellationToken);

        // A false subject enrols the whole audience by implication — no row is ever read.
        if (requiresEnrollment is false) return new TopicEnrollmentDto(audience, audience);

        if (activeYear is null || audience.Length == 0) return new TopicEnrollmentDto(audience, []);

        var enrolled = await db.StudentTopicAssignments
            .AsNoTracking()
            .Where(x => x.TopicId == query.TopicId && x.PeriodId == activeYear.Id)
            .Select(x => x.StudentId)
            .ToArrayAsync(cancellationToken);

        var enrolledSet = enrolled.ToHashSet();

        // Filtered from the roster rather than from the rows, so Eligible is a genuine
        // subset of Audience even if a row survives a grade change.
        return new TopicEnrollmentDto(audience, audience.Where(enrolledSet.Contains).ToArray());
    }
}
