using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValuesByParent;

public sealed record GetCodedValuesByParent(
    Guid? ParentId,
    string? ParentCode,
    IReadOnlyDictionary<string, string>? AttributeFilters = null,
    bool IncludeDisabled = false)
    : IQuery<CodedValueDto[]>;
