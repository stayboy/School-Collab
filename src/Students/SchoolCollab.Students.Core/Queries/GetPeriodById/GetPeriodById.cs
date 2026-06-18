using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.GetPeriodById;

public sealed record GetPeriodById(Guid Id) : IQuery<PeriodDto?>;