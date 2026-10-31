namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// One student matched by <c>GET /students/by-target</c> when the caller asks for names
/// (<c>withNames=true</c>): the id the default <c>Guid[]</c> shape already carries, plus the
/// two name columns the audience panel renders. The id-only shape stays the wire default so
/// the publish path keeps reading exactly what it always did.
/// </summary>
public sealed record TargetStudentDto(Guid Id, string FirstName, string LastName);
