using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// The assignment's values per template constraint dimension (spec §3.1.1) — the input the
/// applicability matcher intersects candidates against. Built once from data the caller already holds:
/// the assignment's target reference ids (grade levels, streams, activity groups), its
/// <see cref="Assignment.TopicId"/> (the subject dimension) and its
/// <see cref="Assignment.AssignmentType"/>.
///
/// <para>A dimension the assignment has <b>no</b> values for is one it lacks: a candidate constrained
/// in it is not applicable (never an exception — an unknown dimension is simply absent). Values are
/// reference ids as their canonical <c>D</c>-format strings — the constraint store holds one
/// <c>Value</c> string per row and no separate <c>RefId</c> (Round A's as-built shape), so an entity
/// dimension's value IS the target's reference id. <see cref="TemplateConstraintDimensions.AssignmentType"/>
/// compares the enum name and <see cref="TemplateConstraintDimensions.Subject"/> compares the topic id.
/// Matching is case-insensitive and trimmed.</para>
/// </summary>
public sealed class AssignmentTemplateContext
{
    private readonly Dictionary<string, HashSet<string>> _values;

    private AssignmentTemplateContext(Dictionary<string, HashSet<string>> values) => _values = values;

    /// <summary>An assignment with no dimensions at all — only the tenant default applies.</summary>
    public static AssignmentTemplateContext Empty { get; } =
        new(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Builds the dimension values from the assignment's authored target ids. A template is never
    /// scoped to an individual student (spec §7), so student and all-students targets are not
    /// dimensions and are simply not passed.
    /// </summary>
    public static AssignmentTemplateContext Create(
        IEnumerable<Guid> gradeLevelIds,
        IEnumerable<Guid> streamIds,
        IEnumerable<Guid> activityGroupIds,
        Guid? topicId,
        AssignmentType assignmentType)
    {
        ArgumentNullException.ThrowIfNull(gradeLevelIds);
        ArgumentNullException.ThrowIfNull(streamIds);
        ArgumentNullException.ThrowIfNull(activityGroupIds);

        var values = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        Add(values, TemplateConstraintDimensions.GradeLevel, gradeLevelIds);
        Add(values, TemplateConstraintDimensions.Stream, streamIds);
        Add(values, TemplateConstraintDimensions.ActivityGroup, activityGroupIds);

        if (topicId is { } topic && topic != Guid.Empty)
        {
            Add(values, TemplateConstraintDimensions.Subject, new[] { topic });
        }

        Add(values, TemplateConstraintDimensions.AssignmentType, new[] { assignmentType.ToString() });

        return new AssignmentTemplateContext(values);
    }

    /// <summary>Projects a live <see cref="Assignment"/> onto the constraint dimensions.</summary>
    public static AssignmentTemplateContext FromAssignment(Assignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return Create(
            TargetIds(assignment, TargetKind.GradeLevel),
            TargetIds(assignment, TargetKind.Stream),
            TargetIds(assignment, TargetKind.ActivityGroup),
            assignment.TopicId,
            assignment.AssignmentType);
    }

    /// <summary>
    /// Whether this assignment carries <paramref name="value"/> in <paramref name="dimension"/>.
    /// <see langword="false"/> for a dimension the assignment lacks — the "a constrained dimension the
    /// assignment lacks ⇒ not applicable" rule, and the degrade path for an unknown dimension.
    /// </summary>
    public bool Intersects(string dimension, string value) =>
        !string.IsNullOrWhiteSpace(value)
        && _values.TryGetValue(dimension, out var dimensionValues)
        && dimensionValues.Contains(value.Trim());

    private static IEnumerable<Guid> TargetIds(Assignment assignment, TargetKind kind) =>
        assignment.Targets
            .Where(t => t.Kind == kind && t.RefId.HasValue)
            .Select(t => t.RefId!.Value);

    private static void Add(
        Dictionary<string, HashSet<string>> values, string dimension, IEnumerable<Guid> ids)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            set.Add(id.ToString("D"));
        }

        if (set.Count > 0)
        {
            values[dimension] = set;
        }
    }

    private static void Add(
        Dictionary<string, HashSet<string>> values, string dimension, IEnumerable<string> strings)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in strings)
        {
            set.Add(value);
        }

        if (set.Count > 0)
        {
            values[dimension] = set;
        }
    }
}

/// <summary>
/// The single spelling of each template constraint dimension inside Assignments.Core — the constraint
/// store's <c>TemplateConstraintDimension</c> names, mirrored as strings for the same
/// no-cross-context-reference reason as <see cref="NotificationTemplateConstraint"/>.
/// </summary>
public static class TemplateConstraintDimensions
{
    /// <summary>Grade level (audience).</summary>
    public const string GradeLevel = "GradeLevel";

    /// <summary>Stream (audience).</summary>
    public const string Stream = "Stream";

    /// <summary>Activity group (audience).</summary>
    public const string ActivityGroup = "ActivityGroup";

    /// <summary>Subject / topic (content) — matched against the assignment's topic id.</summary>
    public const string Subject = "Subject";

    /// <summary>Assignment type (content) — matched against the enum name.</summary>
    public const string AssignmentType = "AssignmentType";
}
