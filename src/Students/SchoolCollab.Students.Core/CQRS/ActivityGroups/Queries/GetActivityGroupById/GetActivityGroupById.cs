using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetActivityGroupById;

public sealed record GetActivityGroupById(Guid Id) : IQuery<ActivityGroupDto?>;
