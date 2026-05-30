using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Queries.GetCodedValueById;

public sealed record GetCodedValueById(Guid Id) : IQuery<CodedValueDto>;
