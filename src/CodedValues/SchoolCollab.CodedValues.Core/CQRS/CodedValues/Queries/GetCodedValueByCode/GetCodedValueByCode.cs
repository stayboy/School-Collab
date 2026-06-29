using SchoolCollab.Core.CQRS;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Queries.GetCodedValueByCode;

public sealed record GetCodedValueByCode(string Code, Guid? ParentId = null) : IQuery<CodedValueDto?>;