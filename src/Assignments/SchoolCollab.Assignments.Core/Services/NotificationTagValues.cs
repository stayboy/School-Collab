using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// The single input record the <see cref="NotificationTemplateRenderer"/> renders from
/// (Q-E, round notification-templates-b1). The broadcaster assembles it from the widened
/// <see cref="AssignmentPublishedContext"/> (assignment content — zero extra reads) plus
/// the per-recipient enrichment and link values, and hands it to the pure renderer — one
/// defined input type, never a parameter list.
/// <para>Every enrichment field degrades to <see cref="string.Empty"/> (spec §3.4): a name
/// that cannot be resolved never fails the send.</para>
/// </summary>
public sealed record NotificationTagValues
{
    /// <summary>The assignment title — <c>{{Title}}</c>.</summary>
    public required string AssignmentTitle { get; init; }

    /// <summary>The assignment description — <c>{{Description}}</c> source (rendered in full;
    /// the built-in preview body caps its own excerpt).</summary>
    public string? Description { get; init; }

    /// <summary>The assignment due date — <c>{{DueDate}}</c>; null = tag renders empty.</summary>
    public DateTimeOffset? DueDate { get; init; }

    /// <summary>Student-facing instruction HTML — <c>{{Instructions}}</c> (email-only).</summary>
    public string? Instructions { get; init; }

    /// <summary>The guardian contact's display name — <c>{{GuardianName}}</c>; "" when
    /// unresolved or when the recipient is student-owned.</summary>
    public string ContactName { get; init; } = string.Empty;

    /// <summary>The guardian's role text (<c>Primary</c> / <c>CC</c>) — <c>{{GuardianRole}}</c>;
    /// "" for a student-owned recipient (no guardian link).</summary>
    public string GuardianRole { get; init; } = string.Empty;

    /// <summary>The ward student's display name — <c>{{StudentName}}</c>; "" when unresolved.</summary>
    public string StudentName { get; init; } = string.Empty;

    /// <summary>The tenant display name — <c>{{SchoolName}}</c>; "" when unavailable.</summary>
    public string SchoolName { get; init; } = string.Empty;

    /// <summary>The recipient's link target — <c>{{Link}}</c>. Relative
    /// <c>/deeplink/{token}</c> by default; absolute when the Families public base URL is set.
    /// Empty when the recipient has no unexpired token (those rows are skipped before render).</summary>
    public string Link { get; init; } = string.Empty;

    /// <summary>When the recipient's link expires — <c>{{LinkExpiry}}</c>; null = tag renders empty.</summary>
    public DateTimeOffset? LinkExpiry { get; init; }
}
