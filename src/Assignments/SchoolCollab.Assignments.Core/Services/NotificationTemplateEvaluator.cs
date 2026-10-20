namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// The applicability matcher (spec §3.1.1 / §8, round notification-templates-b2c). Pure and static:
/// it takes the tenant's candidates and the assignment's dimensions and answers which template renders
/// (or none, meaning the built-in default). No I/O, no state, no clock.
///
/// <para><b>Applicability.</b> A candidate applies iff <b>every</b> constrained dimension intersects
/// the assignment — and a candidate constrained in a dimension the assignment lacks does not apply.</para>
///
/// <para><b>Order.</b> By <b>specificity</b>: the count of <b>distinct constrained dimensions</b>, not
/// the row count (three GradeLevel values on one candidate is ONE dimension, so a two-dimension
/// candidate outranks it). <b>Tie (spec §3.7 / Q67): the tenant default wins.</b> When two or more
/// candidates share the top specificity the tenant-wide unconstrained template is selected ahead of
/// them — ambiguity resolves to the conservative, admin-authored baseline rather than an arbitrary
/// pick between equally specific rows, and it is what the publish dialog pre-selects. With no default
/// available the <b>oldest</b> <see cref="NotificationTemplateCandidate.CreatedAt"/> wins —
/// <c>UpdatedAt</c> moves on edit and would be non-deterministic. The template id is the final
/// tie-break so the order is total.</para> </summary>
public static class NotificationTemplateEvaluator
{
    /// <summary>
    /// The applicable candidates, most specific first. Empty when none apply — the caller renders the
    /// built-in default.
    /// </summary>
    public static IReadOnlyList<NotificationTemplateCandidate> Rank(
        IReadOnlyList<NotificationTemplateCandidate> candidates,
        AssignmentTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);

        var applicable = candidates
            .Where(candidate => IsApplicable(candidate, context))
            .ToList();

        var ranked = applicable
            .OrderByDescending(Specificity)
            .ThenBy(candidate => candidate.CreatedAt)
            .ThenBy(candidate => candidate.TemplateId)
            .ToList();

        // §3.7 / Q67 tie rule: "most specific match, tie → tenant default". Two or more equally
        // specific constrained candidates are ambiguous, so the tenant-wide default wins outright
        // instead of the oldest of the tied rows. The default is specificity 0 and therefore never
        // shares a group with a constrained candidate — it is promoted OVER the tied group, not
        // ordered inside it (an intra-group "default first" key would be a no-op by construction).
        // With no default to fall back on, the oldest CreatedAt wins.
        if (ranked.Count >= 2
            && Specificity(ranked[0]) == Specificity(ranked[1])
            && Specificity(ranked[0]) > 0)
        {
            var tenantDefault = ranked.FirstOrDefault(candidate => Specificity(candidate) == 0);
            if (tenantDefault is not null)
            {
                ranked.Remove(tenantDefault);
                ranked.Insert(0, tenantDefault);
            }
        }

        return ranked;
    }

    /// <summary>
    /// The winning candidate for <paramref name="context"/>, or <see langword="null"/> when nothing
    /// applies (built-in default).
    /// </summary>
    public static NotificationTemplateCandidate? Select(
        IReadOnlyList<NotificationTemplateCandidate> candidates,
        AssignmentTemplateContext context) =>
        Rank(candidates, context).FirstOrDefault();

    /// <summary>
    /// The specificity of one candidate: its number of <b>distinct</b> constrained dimensions.
    /// <c>0</c> is the tenant default.
    /// </summary>
    public static int Specificity(NotificationTemplateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return candidate.Constraints
            .Select(c => c.Dimension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    /// <summary>
    /// Whether every constrained dimension of <paramref name="candidate"/> intersects
    /// <paramref name="context"/>. Several values on the same dimension are an <b>alternative</b> set
    /// ("Grade 3 or Grade 4"), so the dimension intersects when any one of its values does; a
    /// dimension the assignment lacks never intersects. An unconstrained candidate (the tenant
    /// default) always applies.
    /// </summary>
    public static bool IsApplicable(NotificationTemplateCandidate candidate, AssignmentTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var dimension in candidate.Constraints.GroupBy(c => c.Dimension, StringComparer.OrdinalIgnoreCase))
        {
            if (!dimension.Any(value => context.Intersects(dimension.Key, value.Value)))
            {
                return false;
            }
        }

        return true;
    }
}
