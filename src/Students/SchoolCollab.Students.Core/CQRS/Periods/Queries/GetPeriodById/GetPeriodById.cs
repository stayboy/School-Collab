using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.GetPeriodById;

public sealed record GetPeriodById(Guid Id) : IQuery<PeriodDto?>;