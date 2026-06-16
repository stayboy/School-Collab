using System.ComponentModel;

namespace SchoolCollab.AI;

/// <summary>
/// Represents a single child value to create in a bulk creation request.
/// </summary>
public record BulkChildItem(
    [Description("Short uppercase code for the child value, e.g. US")] string Code,
    [Description("Display name, e.g. United States")] string Name,
    [Description("A concise description that adds context beyond the name. IMPORTANT: always include a description when one can be reasonably inferred from the category (e.g., for a country: 'ISO 3166-1 numeric code 840'; for a language: 'West Germanic language'). Even a short description is better than blank. Omit only when no meaningful description exists.")] string? Description = null,
    [Description("Sort order starting from 1")] int DisplayOrder = 0);