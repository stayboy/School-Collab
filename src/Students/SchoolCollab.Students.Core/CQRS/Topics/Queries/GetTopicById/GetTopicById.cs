using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Queries.GetTopicById;

public sealed record GetTopicById(Guid Id) : IQuery<TopicDto?>;