using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardiansByStudent;

public sealed record ListGuardiansByStudent(Guid StudentId) : IQuery<StudentGuardianViewDto[]>;
