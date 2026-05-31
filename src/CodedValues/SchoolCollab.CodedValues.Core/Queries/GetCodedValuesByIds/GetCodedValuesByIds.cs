using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Queries.GetCodedValuesByIds;

public sealed record GetCodedValuesByIds(Guid[] Ids) : IQuery<CodedValueDto[]>;
