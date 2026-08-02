using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.ListActivityGroups;

public sealed record ListActivityGroups : IQuery<ActivityGroupDto[]>;
