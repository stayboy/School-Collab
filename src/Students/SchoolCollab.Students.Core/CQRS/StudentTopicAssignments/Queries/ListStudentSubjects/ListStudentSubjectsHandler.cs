using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentSubjects;

/// <summary>
/// Builds the §6 union (FR-ER-16): a student's grade subjects, keeping every
/// <c>false</c> subject unconditionally (membership IS the enrolment, FR-ER-1) and a
/// <c>true</c> subject only when the student holds a <c>StudentTopicAssignment</c> for the
/// <b>active academic year</b> (FR-ER-3 — an exact period-id match, so last year's row does
/// not carry).
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

        return subjects
            .Where(x => !x.RequiresEnrollment || enrolled.Contains(x.TopicId))
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
