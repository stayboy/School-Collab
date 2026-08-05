namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Number of linked (non-deleted) students for a guardian. Returned by the
/// bulk <c>GET /guardians/student-counts</c> endpoint so the guardians landing
/// page can render a "N students" cell without an N+1 per-guardian fetch.
/// </summary>
public sealed record StudentCountDto(Guid GuardianId, int Count);
