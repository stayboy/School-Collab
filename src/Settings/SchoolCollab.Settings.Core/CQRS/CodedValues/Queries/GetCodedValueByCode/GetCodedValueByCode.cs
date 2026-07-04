using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueByCode;

public sealed record GetCodedValueByCode(string Code, Guid? ParentId = null) : IQuery<CodedValueDto?>;