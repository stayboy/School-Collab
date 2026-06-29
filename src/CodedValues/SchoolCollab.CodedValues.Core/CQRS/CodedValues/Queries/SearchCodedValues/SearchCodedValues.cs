using SchoolCollab.Core.CQRS;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Queries.SearchCodedValues;

/// <summary>
/// Searches coded values (parents and/or children) by matching text
/// against Code, Name, and Description using case-insensitive ILIKE.
/// </summary>
public sealed record SearchCodedValues(
    string SearchText,
    Guid? ParentId = null,
    bool IncludeDisabled = false)
    : IQuery<CodedValueDto[]>;