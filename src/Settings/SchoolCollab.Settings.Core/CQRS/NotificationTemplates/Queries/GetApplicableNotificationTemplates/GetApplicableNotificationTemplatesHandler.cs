using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetApplicableNotificationTemplates;

/// <summary>
/// Builds the grade-detail card's payload (spec §3.11): the current tenant's live templates that can
/// apply to <see cref="GetApplicableNotificationTemplates.GradeLevelId"/>, grouped into the eight
/// authorable kind × channel slots. Read through the tenant query filter, so another tenant's rows can
/// never appear.
/// <para>
/// Order within a slot mirrors the matcher's sort <b>keys</b> (spec §3.1.1) but not its
/// <b>selection</b> rule: specificity descending (distinct constrained dimensions, never row count),
/// then <c>CreatedAt</c> ascending, then id. The Q67 "tie → tenant default" promotion is deliberately
/// absent — the card carries that meaning in its <c>IsDefault</c> highlight, and a specificity sort
/// alone would otherwise place the default, at zero, last.
/// </para>
/// </summary>
public sealed class GetApplicableNotificationTemplatesHandler(
    SettingsDbContext db,
    ICodedValueRepository codedValues,
    ICodedValueResolver codedValueResolver,
    ITenantProvider tenantProvider)
    : IQueryHandler<GetApplicableNotificationTemplates, ApplicableNotificationTemplatesDto>
{
    /// <summary>The four authorable kinds, in spec order — the card's slot order.</summary>
    private static readonly NotificationTemplateKind[] Kinds =
    [
        NotificationTemplateKind.Publish,
        NotificationTemplateKind.Reminder,
        NotificationTemplateKind.Completion,
        NotificationTemplateKind.Overdue,
    ];

    /// <summary>
    /// The three authorable channels (redesign spec §3.10 — WhatsApp became a real template mode).
    /// It used to be excluded defensively; the store now accepts it, so it is a slot like any other.
    /// </summary>
    private static readonly NotificationChannel[] Channels =
    [
        NotificationChannel.Email,
        NotificationChannel.SMS,
        NotificationChannel.WhatsApp,
    ];

    public async Task<ApplicableNotificationTemplatesDto> HandleAsync(
        GetApplicableNotificationTemplates query, CancellationToken ct = default)
    {
        var gradeLevelId = query.GradeLevelId.ToString("D");
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var templates = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .AsNoTracking()
            .ToListAsync(ct);

        // One resolution per coded value, however many slots and constraints mention it.
        var displays = new Dictionary<Guid, string>();
        var slots = new List<ApplicableTemplateSlotDto>(Kinds.Length * Channels.Length);

        foreach (var kind in Kinds)
        {
            foreach (var channel in Channels)
            {
                var applicable = templates
                    .Where(t => t.Kind == kind && t.Channel == channel && AppliesToGrade(t, gradeLevelId))
                    // Distinct constrained DIMENSIONS descending — never the row count: three
                    // GradeLevel values on one candidate are ONE dimension, so a candidate
                    // constraining two dimensions outranks it (spec §3.1.1).
                    .OrderByDescending(DistinctDimensionCount)
                    .ThenBy(t => t.CreatedAt)
                    .ThenBy(t => t.Id)
                    .ToList();

                var rows = new List<ApplicableTemplateDto>(applicable.Count);
                foreach (var template in applicable)
                {
                    var constraints = new List<ApplicableConstraintDto>(template.Constraints.Count);
                    foreach (var constraint in template.Constraints
                                 .OrderBy(c => c.Dimension)
                                 .ThenBy(c => c.Value, StringComparer.Ordinal))
                    {
                        constraints.Add(new ApplicableConstraintDto(
                            constraint.Dimension,
                            constraint.Value,
                            await ResolveDisplayAsync(constraint, tenantId, displays, ct)));
                    }

                    rows.Add(new ApplicableTemplateDto(
                        template.Id,
                        template.Subject,
                        template.Constraints.Count == 0,
                        constraints));
                }

                slots.Add(new ApplicableTemplateSlotDto(kind, channel, rows));
            }
        }

        return new ApplicableNotificationTemplatesDto(slots);
    }

    /// <summary>
    /// Whether <paramref name="template"/> can apply to the queried grade: it carries no
    /// <see cref="TemplateConstraintDimension.GradeLevel"/> constraint, or one of them is this grade.
    /// <para>
    /// The comparison is trimmed and <see cref="StringComparison.OrdinalIgnoreCase"/>. Casing is the
    /// live variable, not whitespace: <c>TemplateConstraint.Create</c> trims every value at write and
    /// preserves its casing, while <see cref="Guid.ToString(string)"/> in <c>D</c> format is lowercase.
    /// The matcher compares case-insensitively, so a hand-authored uppercase id fires at publish time
    /// and must not vanish from this card. The read-side trim guards a value not written through the
    /// store; every store-reachable value is already trimmed.
    /// </para>
    /// </summary>
    private static bool AppliesToGrade(TenantNotificationTemplate template, string gradeLevelId)
    {
        var gradeConstraints = template.Constraints
            .Where(c => c.Dimension == TemplateConstraintDimension.GradeLevel)
            .ToList();

        return gradeConstraints.Count == 0
            || gradeConstraints.Any(c =>
                string.Equals(c.Value.Trim(), gradeLevelId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A candidate's specificity: its number of <b>distinct</b> constrained dimensions — the matcher's
    /// own key (spec §3.1.1). Zero is the tenant default. Never the constraint row count.
    /// </summary>
    private static int DistinctDimensionCount(TenantNotificationTemplate template) =>
        template.Constraints.Select(c => c.Dimension).Distinct().Count();

    /// <summary>
    /// The label to show for one constraint. The code-backed dimensions store a coded-value id, so the
    /// tenant-resolved coded value name is the label; <c>AssignmentType</c> already stores the enum
    /// name. Anything unresolvable — an id with no coded value, a value that is not an id, a blank
    /// resolved name — falls back to the raw stored value, so the card never blanks a constraint and a
    /// stale reference never fails the read (spec §3.11).
    /// </summary>
    private async Task<string> ResolveDisplayAsync(
        TemplateConstraint constraint,
        Guid tenantId,
        Dictionary<Guid, string> resolved,
        CancellationToken ct)
    {
        if (constraint.Dimension == TemplateConstraintDimension.AssignmentType
            || !Guid.TryParse(constraint.Value, out var codedValueId))
        {
            return constraint.Value;
        }

        if (resolved.TryGetValue(codedValueId, out var cached))
            return cached;

        var codedValue = await codedValues.GetAsync(codedValueId, ct);
        var display = codedValue is null
            ? constraint.Value
            : (await codedValueResolver.ResolveAsync(codedValue, tenantId, ct)).Name;

        if (string.IsNullOrWhiteSpace(display))
            display = constraint.Value;

        resolved[codedValueId] = display;
        return display;
    }
}
