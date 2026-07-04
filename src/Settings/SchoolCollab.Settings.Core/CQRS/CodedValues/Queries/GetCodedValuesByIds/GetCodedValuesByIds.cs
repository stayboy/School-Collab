using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValuesByIds;

public sealed record GetCodedValuesByIds(Guid[] Ids) : IQuery<CodedValueDto[]>;
