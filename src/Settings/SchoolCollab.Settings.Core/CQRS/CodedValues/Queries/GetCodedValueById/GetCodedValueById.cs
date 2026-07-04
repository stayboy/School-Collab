using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueById;

public sealed record GetCodedValueById(Guid Id) : IQuery<CodedValueDto?>;
