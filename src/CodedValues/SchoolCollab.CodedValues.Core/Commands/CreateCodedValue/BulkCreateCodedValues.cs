using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.CreateCodedValue;

/// <summary>
/// Command to create multiple child coded values under a parent in a single transaction.
/// </summary>
public sealed record BulkCreateCodedValues(
    Guid ParentId,
    IReadOnlyList<BulkCreateChildItem> Children) : ICommand;

/// <summary>
/// A single child value to create as part of a bulk creation request.
/// </summary>
public sealed record BulkCreateChildItem(
    string Code,
    string Name,
    string? Description,
    int DisplayOrder);