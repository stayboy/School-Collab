using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Queries.ListEnrollmentsByStudents;

/// <summary>
/// Bulk variant of <c>ListEnrollmentsByStudent</c>: returns all non-deleted
/// enrollments for every requested student in a single query. Used by the
/// client-side student-list enrichment (<c>EnrichStudentsAsync</c>) to avoid
/// N+1 per-student API round-trips when hydrating <c>CurrentGrade</c>.
/// </summary>
public sealed record ListEnrollmentsByStudents(Guid[] StudentIds) : IQuery<StudentEnrollmentDto[]>;
