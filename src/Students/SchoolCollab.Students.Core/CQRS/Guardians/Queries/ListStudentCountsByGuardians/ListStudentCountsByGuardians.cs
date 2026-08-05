using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListStudentCountsByGuardians;

/// <summary>
/// Bulk student-count query: returns the number of linked (non-deleted)
/// students for every requested guardian in a single DB round-trip. Used by
/// the client-side guardians-list enrichment to avoid an N+1 per-guardian API
/// call when hydrating the guardians landing page's "N students" column.
/// </summary>
public sealed record ListStudentCountsByGuardians(Guid[] GuardianIds) : IQuery<StudentCountDto[]>;
