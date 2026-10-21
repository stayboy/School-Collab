using System.Text.Json;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Services;

/// <summary>
/// Serialises a template's constraint set into the compact JSON stored on
/// <see cref="TemplateAuditEntry.PreviousConstraints"/> / <see cref="TemplateAuditEntry.NewConstraints"/>.
/// Shared by the upsert and delete handlers so the before/after summaries in the
/// audit log are always comparable (spec §3.9).
/// </summary>
public static class TemplateConstraintSummary
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Returns the audit-log summary for <paramref name="constraints"/>, ordered by
    /// dimension so two logically equal sets always serialise identically.
    /// </summary>
    public static string Serialize(IEnumerable<TemplateConstraint> constraints) =>
        JsonSerializer.Serialize(
            constraints
                .OrderBy(c => c.Dimension)
                .ThenBy(c => c.Value, StringComparer.Ordinal)
                .Select(c => new { Dimension = c.Dimension.ToString(), c.Value }),
            Options);

    /// <summary>
    /// Returns the audit-log summary for a request's constraint specs (the
    /// before-row does not exist yet), ordered identically to
    /// <see cref="Serialize"/>.
    /// </summary>
    /// <summary>
    /// Returns the audit-log summary for a request's constraint specs (the
    /// before-row does not exist yet), ordered identically to
    /// <see cref="Serialize(IEnumerable{TemplateConstraint})"/>.
    /// </summary>
    public static string Serialize(IEnumerable<(TemplateConstraintDimension Dimension, string Value)> constraints) =>
        JsonSerializer.Serialize(
            constraints
                .OrderBy(c => c.Dimension)
                .ThenBy(c => c.Value, StringComparer.Ordinal)
                .Select(c => new { Dimension = c.Dimension.ToString(), c.Value }),
            Options);

    /// <summary>
    /// An order- and case-insensitive identity for a constraint set, used by adopt/promote to answer
    /// "is this exact set already present in the target scope?" (redesign spec §3.8). Two sets that a
    /// user would call identical produce the same string.
    /// </summary>
    public static string Identity(IEnumerable<TemplateConstraint> constraints) =>
        string.Join(
            '|',
            constraints
                .Select(c => $"{c.Dimension}:{c.Value.Trim().ToLowerInvariant()}")
                .OrderBy(x => x, StringComparer.Ordinal));
}
