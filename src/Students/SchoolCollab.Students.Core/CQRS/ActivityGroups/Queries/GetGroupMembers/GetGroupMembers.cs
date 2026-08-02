using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetGroupMembers;

public sealed record GetGroupMembers(Guid ActivityGroupId) : IQuery<MembershipDto[]>;
