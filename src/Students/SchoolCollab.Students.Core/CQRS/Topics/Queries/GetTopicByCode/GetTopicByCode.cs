using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Queries.GetTopicByCode;

public sealed record GetTopicByCode(string Code) : IQuery<TopicDto?>;