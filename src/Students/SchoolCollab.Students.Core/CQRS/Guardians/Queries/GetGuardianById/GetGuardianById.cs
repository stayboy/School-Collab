using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.GetGuardianById;

public sealed record GetGuardianById(Guid Id) : IQuery<GuardianDto?>;
