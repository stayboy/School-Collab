using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListEnrollmentsByStudent;

public sealed record ListEnrollmentsByStudent(Guid StudentId) : IQuery<StudentEnrollmentDto[]>;