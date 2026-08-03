using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetStudentGroups;

public sealed record GetStudentGroups(Guid StudentId) : IQuery<ActivityGroupDto[]>;
