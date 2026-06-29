using SchoolCollab.Core.CQRS;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Queries.GetCodedValuesByIds;

public sealed record GetCodedValuesByIds(Guid[] Ids) : IQuery<CodedValueDto[]>;
