using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments;

/// <summary>
/// The single source of truth for subject availability
/// (subject-period-exception-model.md v3 §2.3):
/// <c>available(owner, topic, date) = EXISTS bridge AND NOT EXISTS exception containing date</c>.
///
/// <para><b>One date test.</b> <c>(start_date IS NULL OR start_date &lt;= date) AND
/// (end_date IS NULL OR end_date &gt;= date)</c>, with a null bound meaning open on that
/// side. The exception's <c>Division</c> is descriptive only and does not participate in
/// matching.</para>
///
/// <para>Shared by the two cached availability readers (the FR-58 feed readers) so the
/// predicate and the cache key cannot drift apart. It replaces v1's "active period
/// id set" mechanism wholesale —
/// the active-period resolution helpers and the cache-key period segment are gone: with
/// no period reference in the predicate there is no active-period lookup at all, no id
/// set, and no period segment in any cache key
/// (subject-period-exception-model.md v3 §0 decision 9).</para>
/// </summary>
internal static class SubjectAvailability
{
    /// <summary>
    /// Topic ids excepted for a grade on <paramref name="effectiveDate"/>. The tenant
    /// filter is applied explicitly because the active tenant context is lost inside a
    /// HybridCache factory; the "SoftDelete" filter stays on, so a removed exception no
    /// longer excludes anything.
    /// </summary>
    public static Task<Guid[]> ExceptedTopicIdsForGradeAsync(
        StudentsDbContext db,
        Guid tenantId,
        Guid gradeLevelId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken) =>
        ExceptedTopicIdsAsync(db, tenantId, effectiveDate, b => b.GradeLevelId == gradeLevelId, cancellationToken);

    /// <summary>Topic ids excepted for an activity group on <paramref name="effectiveDate"/>.</summary>
    public static Task<Guid[]> ExceptedTopicIdsForGroupAsync(
        StudentsDbContext db,
        Guid tenantId,
        Guid activityGroupId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken) =>
        ExceptedTopicIdsAsync(db, tenantId, effectiveDate, b => b.ActivityGroupId == activityGroupId, cancellationToken);

    private static async Task<Guid[]> ExceptedTopicIdsAsync(
        StudentsDbContext db,
        Guid tenantId,
        DateOnly effectiveDate,
        Expression<Func<SubjectEnrollmentException, bool>> ownerFilter,
        CancellationToken cancellationToken) =>
        await db.SubjectEnrollmentExceptions
            .IgnoreQueryFilters(["Tenant"])
            .Where(ownerFilter)
            .Where(b => b.TenantId == tenantId
                && (b.StartDate == null || b.StartDate <= effectiveDate)
                && (b.EndDate == null || b.EndDate >= effectiveDate))
            .Select(b => b.TopicId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
}
