using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardianCountsByStudents;

/// <summary>
/// Bulk guardian-count query: returns the number of linked (non-deleted)
/// guardians for every requested student in a single DB round-trip. Used by
/// the client-side student-list enrichment (<c>EnrichStudentsAsync</c>) to
/// avoid an N+1 per-student API call when hydrating the landing page's
/// "N guardians" column.
/// </summary>
public sealed record ListGuardianCountsByStudents(Guid[] StudentIds) : IQuery<GuardianCountDto[]>;
