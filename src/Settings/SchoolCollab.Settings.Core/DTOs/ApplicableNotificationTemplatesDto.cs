using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// The grade-detail "Notification templates" card's payload (spec §3.11; round
/// notification-templates-b3a): every authorable kind × channel slot for one grade, each carrying the
/// tenant templates whose constraints <b>can apply</b> to it.
/// <para>
/// All eight slots (4 kinds × <see cref="NotificationChannel.Email"/> and
/// <see cref="NotificationChannel.SMS"/>) are always present, empty or not, so the card renders the
/// trio uniformly. There is no WhatsApp slot: <c>TenantNotificationTemplate.Create</c> refuses a
/// WhatsApp template row (WhatsApp recipients render the SMS body, spec §2), so no such row exists to
/// report.
/// </para>
/// </summary>
public sealed record ApplicableNotificationTemplatesDto(
    IReadOnlyList<ApplicableTemplateSlotDto> Slots);

/// <summary>One authorable kind × channel slot and the templates applicable to the queried grade.</summary>
public sealed record ApplicableTemplateSlotDto(
    NotificationTemplateKind Kind,
    NotificationChannel Channel,
    IReadOnlyList<ApplicableTemplateDto> Templates);

/// <summary>
/// One template that can apply to the queried grade, most specific first.
/// <see cref="IsDefault"/> marks the tenant default — the slot's only row with no constraints — which
/// the card highlights in place, rather than promoting it to the head of the list (that promotion is
/// the matcher's publish-time selection rule, spec §3.7/Q67).
/// <see cref="TenantId"/> / <see cref="OwnerName"/> say whether the row is tenant-owned or part of the
/// system blueprint (redesign spec §3.7), so the card can mark a system row.
/// </summary>
public sealed record ApplicableTemplateDto(
    Guid Id,
    string? Subject,
    bool IsDefault,
    IReadOnlyList<ApplicableConstraintDto> Constraints,
    Guid? TenantId,
    string OwnerName);

/// <summary>
/// One constraint of an applicable template. <see cref="Display"/> is the label for
/// <see cref="Value"/>: the tenant-resolved coded-value name for the code-backed dimensions
/// (<see cref="TemplateConstraintDimension.GradeLevel"/>, <c>Stream</c>, <c>ActivityGroup</c>,
/// <c>Subject</c>), the enum name for <see cref="TemplateConstraintDimension.AssignmentType"/>, and
/// the raw stored value whenever that cannot be resolved — a constraint is never blank and never
/// fails the read.
/// </summary>
public sealed record ApplicableConstraintDto(
    TemplateConstraintDimension Dimension,
    string Value,
    string Display);
