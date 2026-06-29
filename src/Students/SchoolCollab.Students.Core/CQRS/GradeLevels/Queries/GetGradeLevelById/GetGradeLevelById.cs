using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.GetGradeLevelById;

public sealed record GetGradeLevelById(Guid Id) : IQuery<GradeLevelDto?>;