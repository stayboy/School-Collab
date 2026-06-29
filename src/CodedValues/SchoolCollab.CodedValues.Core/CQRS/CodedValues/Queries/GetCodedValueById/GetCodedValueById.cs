using SchoolCollab.Core.CQRS;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Queries.GetCodedValueById;

public sealed record GetCodedValueById(Guid Id) : IQuery<CodedValueDto?>;
