using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListStudentsForGuardian;

public sealed record ListStudentsForGuardian(Guid GuardianId) : IQuery<StudentDto[]>;
