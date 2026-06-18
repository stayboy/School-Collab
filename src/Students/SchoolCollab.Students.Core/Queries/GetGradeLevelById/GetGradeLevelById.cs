using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.GetGradeLevelById;

public sealed record GetGradeLevelById(Guid Id) : IQuery<GradeLevelDto?>;