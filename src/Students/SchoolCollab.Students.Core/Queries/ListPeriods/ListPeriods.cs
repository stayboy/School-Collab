using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListPeriods;

public sealed record ListPeriods : IQuery<PeriodDto[]>;