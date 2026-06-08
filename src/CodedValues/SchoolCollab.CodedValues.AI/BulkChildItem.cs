using System.ComponentModel;

namespace SchoolCollab.CodedValues.AI;

/// <summary>
/// Represents a single child value to create in a bulk creation request.
/// </summary>
public record BulkChildItem(
    [Description("Short uppercase code for the child value, e.g. US")] string Code,
    [Description("Display name, e.g. United States")] string Name,
    [Description("Optional description")] string? Description = null,
    [Description("Sort order starting from 1")] int DisplayOrder = 0);