using SchoolCollab.Core.CQRS;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Queries.GetCodedValuesByParent;

public sealed record GetCodedValuesByParent(
    Guid? ParentId,
    string? ParentCode,
    IReadOnlyDictionary<string, string>? AttributeFilters = null,
    bool IncludeDisabled = false)
    : IQuery<CodedValueDto[]>;
