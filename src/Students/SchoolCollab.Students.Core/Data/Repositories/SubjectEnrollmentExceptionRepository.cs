using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class SubjectEnrollmentExceptionRepository(StudentsDbContext db)
    : RepositoryBase<SubjectEnrollmentException, StudentsDbContext>(db), ISubjectEnrollmentExceptionRepository
{
    /// <summary>
    /// Removing an exception soft-deletes the row (the "SoftDelete" query filter hides
    /// it from every subsequent read), so the reason a subject was unavailable is
    /// retained.
    /// </summary>
    public async Task SoftDeleteAsync(SubjectEnrollmentException exception, CancellationToken cancellationToken = default)
    {
        exception.MarkAsDeleted();
        await UpdateAsync(exception, cancellationToken);
    }

    public async Task<SubjectEnrollmentExceptionDto[]> ListDtosAsync(
        Guid? gradeLevelId,
        Guid? activityGroupId,
        Guid? topicId = null,
        CancellationToken cancellationToken = default)
    {
        var rows = await Db.SubjectEnrollmentExceptions
            .AsNoTracking()
            .Where(b => (gradeLevelId == null || b.GradeLevelId == gradeLevelId)
                     && (activityGroupId == null || b.ActivityGroupId == activityGroupId)
                     && (topicId == null || b.TopicId == topicId))
            // Open starts first, then by start date — the UI lists the widest span first.
            // Postgres orders NULLs LAST in ASC, so the open-start rows need an explicit
            // null-first sort key; the provider exposes no per-query NULLS FIRST.
            .OrderBy(b => b.StartDate == null ? 0 : 1)
            .ThenBy(b => b.StartDate)
            .ThenBy(b => b.CreatedAt)
            .Select(b => new
            {
                b.Id,
                b.GradeLevelId,
                b.ActivityGroupId,
                b.TopicId,
                b.Division,
                b.StartDate,
                b.EndDate,
                b.Reason,
                b.CreatedAt,
                b.UpdatedAt,
            })
            .ToArrayAsync(cancellationToken);

        return rows.Select(r => new SubjectEnrollmentExceptionDto(
                r.Id,
                r.GradeLevelId,
                r.ActivityGroupId,
                r.TopicId,
                r.Division.ToString(),
                r.StartDate,
                r.EndDate,
                r.Reason,
                r.CreatedAt,
                r.UpdatedAt))
            .ToArray();
    }

    public Task<bool> ExistsAsync(
        Guid? gradeLevelId,
        Guid? activityGroupId,
        Guid topicId,
        AcademicYearDivision division,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken = default) =>
        Db.SubjectEnrollmentExceptions.AnyAsync(
            x => x.GradeLevelId == gradeLevelId
                && x.ActivityGroupId == activityGroupId
                && x.TopicId == topicId
                && x.Division == division
                // Defensive null-safe equality, kept so the C# pre-check reads as the same
                // key the COALESCE expression index uses (an open bound collapses onto a
                // sentinel). EF's own null-parameter translation is already null-safe here —
                // a captured `DateOnly?` becomes a parameter and EF emits the null-aware
                // form — so this coalesce is not load-bearing; it mirrors the index for
                // readability only.
                && (x.StartDate ?? DateOnly.MinValue) == (startDate ?? DateOnly.MinValue)
                && (x.EndDate ?? DateOnly.MaxValue) == (endDate ?? DateOnly.MaxValue),
            cancellationToken);
}
