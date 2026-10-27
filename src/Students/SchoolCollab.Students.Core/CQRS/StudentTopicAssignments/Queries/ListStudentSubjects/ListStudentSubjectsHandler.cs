using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentSubjects;

/// <summary>
/// Builds the §6 union (FR-ER-16): a student's grade subjects, keeping every
/// <c>false</c> subject unconditionally (membership IS the enrolment, FR-ER-1) and every
/// <c>true</c> subject marked with whether the student holds a <c>StudentTopicAssignment</c>
/// for the <b>active academic year</b> (FR-ER-3 — an exact period-id match, so last year's
/// row does not carry).
/// <para><b>A <c>true</c> subject with no row is still returned</b>, with
/// <see cref="StudentSubjectEnrollmentDto.IsEnrolled"/> = <c>false</c>: the student page's
/// "Needs explicit enrolment" band has nothing to render otherwise, and an absent row is
/// exactly the state that band exists to make visible. The flag is additive — every row the
/// pre-layer-2 contract returned is still returned with <c>IsEnrolled = true</c>, so a caller
/// that filters on it sees the same set as before.</para>
/// <para>The grade comes from the student's active enrolment in that year, resolved with
/// <see cref="IStudentEnrollmentRepository.GetActiveEnrollmentByStudentAndPeriodAsync"/>.
/// No active academic year or no active enrolment ⇒ an empty array (200, never 404): there
/// is no union to express, and the caller has no error to handle.</para>
/// <para>Subject rows are the grade's bridge rows effective <b>today</b>, joined to the
/// shared <c>Topics</c> catalogue for the name/code (the bridge repository's contract is
/// bridge-columns-only, so the join lives here — the
/// <c>ListGradeTopicCurriculumByGradeHandler</c> shape).</para>
/// </summary>
public sealed class ListStudentSubjectsHandler(
    StudentsDbContext db,
    IActivePeriodProvider activePeriodProvider,
    IStudentEnrollmentRepository enrollmentRepository)
    : IQueryHandler<ListStudentSubjects, StudentSubjectEnrollmentDto[]>
{
    public async Task<StudentSubjectEnrollmentDto[]> HandleAsync(
        ListStudentSubjects query,
        CancellationToken cancellationToken = default)
    {
        var activeYear = await activePeriodProvider.GetActiveAcademicYearAsync(cancellationToken);
        if (activeYear is null) return [];

        var enrollment = await enrollmentRepository.GetActiveEnrollmentByStudentAndPeriodAsync(
            query.StudentId, activeYear.Id, cancellationToken);
        if (enrollment is null) return [];

        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var subjects = await (
            from a in db.GradeTopicAssignments
            join t in db.Topics on a.TopicId equals t.Id
            where a.GradeLevelId == enrollment.GradeLevelId
                  && a.StartDate <= effectiveDate
                  && (a.EndDate == null || a.EndDate >= effectiveDate)
            orderby a.DisplayOrder, t.DisplayOrder, t.Name
            select new
            {
                AssignmentId = a.Id,
                TopicId = t.Id,
                t.Name,
                t.Code,
                a.RequiresEnrollment
            }).ToArrayAsync(cancellationToken);

        if (subjects.Length == 0) return [];

        // One read for the whole second half of the union — the same table
        // StudentTopicAssignmentRepository.ListByStudentAsync reads, without the DTO hop.
        var enrolledTopicIds = await db.StudentTopicAssignments
            .AsNoTracking()
            .Where(x => x.StudentId == query.StudentId && x.PeriodId == activeYear.Id)
            .Select(x => x.TopicId)
            .ToArrayAsync(cancellationToken);

        var enrolled = enrolledTopicIds.ToHashSet();

        // No WHERE: an un-enrolled `true` subject is a ROW the rail must draw (struck through,
        // "not chosen"), not a row to hide. `false` subjects short-circuit to enrolled because
        // membership is the enrolment, so a stale assignment row on one is never consulted
        // (FR-ER-9: the rows are inert history and the subject reads as "everyone").
        return subjects
            .Select(x => new StudentSubjectEnrollmentDto(
                x.TopicId,
                x.Name,
                x.Code,
                x.AssignmentId,
                x.RequiresEnrollment,
                IsEnrolled: !x.RequiresEnrollment || enrolled.Contains(x.TopicId)))
            .ToArray();
    }
}
