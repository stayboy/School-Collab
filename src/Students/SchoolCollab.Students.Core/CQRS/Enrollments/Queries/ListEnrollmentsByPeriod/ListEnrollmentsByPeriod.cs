using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Queries.ListEnrollmentsByPeriod;

public sealed record ListEnrollmentsByPeriod(Guid PeriodId) : IQuery<StudentEnrollmentDto[]>;