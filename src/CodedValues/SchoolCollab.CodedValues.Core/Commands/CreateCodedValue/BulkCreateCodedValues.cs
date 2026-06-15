using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.CreateCodedValue;

/// <summary>
/// Command to create multiple child coded values under a parent in a single transaction.
/// Skips any codes that already exist under the parent and returns what was created/skipped.
/// </summary>
public sealed record BulkCreateCodedValues(
    Guid ParentId,
    IReadOnlyList<BulkCreateChildItem> Children) : ICommand;

/// <summary>
/// Result of a bulk create operation.
/// </summary>
public sealed record BulkCreateResult(
    int CreatedCount,
    IReadOnlyList<string> SkippedCodes,
    Guid ParentId);

/// <summary>
/// A single child value to create as part of a bulk creation request.
/// </summary>
public sealed record BulkCreateChildItem(
    string Code,
    string Name,
    string? Description,
    int DisplayOrder);