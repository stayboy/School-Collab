using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.ListPeriods;

public sealed record ListPeriods : IQuery<PeriodDto[]>;