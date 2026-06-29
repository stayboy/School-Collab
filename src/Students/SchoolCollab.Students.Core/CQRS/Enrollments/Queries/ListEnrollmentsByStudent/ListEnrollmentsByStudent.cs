using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Queries.ListEnrollmentsByStudent;

public sealed record ListEnrollmentsByStudent(Guid StudentId) : IQuery<StudentEnrollmentDto[]>;