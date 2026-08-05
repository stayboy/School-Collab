namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Number of linked (non-deleted) guardians for a student. Returned by the
/// bulk <c>GET /students/guardian-counts</c> endpoint so the student landing
/// page can render a "N guardians" cell without an N+1 per-student fetch.
/// </summary>
public sealed record GuardianCountDto(Guid StudentId, int Count);
