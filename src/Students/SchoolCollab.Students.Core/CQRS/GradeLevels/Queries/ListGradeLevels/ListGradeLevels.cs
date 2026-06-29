using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevels;

public sealed record ListGradeLevels : IQuery<GradeLevelDto[]>;