using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListEnrollmentsByPeriod;

public sealed record ListEnrollmentsByPeriod(Guid PeriodId) : IQuery<StudentEnrollmentDto[]>;