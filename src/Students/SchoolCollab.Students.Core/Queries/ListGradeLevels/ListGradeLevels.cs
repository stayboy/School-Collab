using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListGradeLevels;

public sealed record ListGradeLevels : IQuery<GradeLevelDto[]>;