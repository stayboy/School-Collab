using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardians;

public sealed record ListGuardians(string? Search = null, Guid? ExcludeStudentId = null, Guid? StudentId = null) : IQuery<GuardianDto[]>;
