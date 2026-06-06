using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Queries.GetCodedValueByCode;

public sealed record GetCodedValueByCode(string Code) : IQuery<CodedValueDto>;