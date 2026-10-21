using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

/// <summary>
/// The editor's draft — everything a create or a replace sends, plus the row identity when editing.
/// Constraints are held as the wire DTOs the editor received (or, on create, as a prefill) and are only
/// persisted by <see cref="TemplateEditor"/>'s Save: a constraint edit is staged, never written on its own
/// (redesign spec §3.6/Q9), so adding a chip can never clobber unsaved body edits.
/// </summary>
public sealed class TemplateFormModel
{
    /// <summary>The row being edited; null until the first successful create.</summary>
    public Guid? Id { get; set; }

    /// <summary>The notification kind (immutable once the row exists).</summary>
    public NotificationTemplateKind Kind { get; set; } = NotificationTemplateKind.Publish;

    /// <summary>The preferred mode of contact (immutable once the row exists).</summary>
    public NotificationChannel Channel { get; set; } = NotificationChannel.Email;

    /// <summary>Subject line — email only; cleared for the plain-text modes by the domain.</summary>
    public string? Subject { get; set; }

    /// <summary>Sanitized HTML for email; plain text for SMS and WhatsApp.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>The staged constraint set (one row per dimension value).</summary>
    public List<TemplateConstraintDto> Constraints { get; } = [];

    /// <summary>Optional free-text reason, copied onto the audit entry.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>When the row was created (edit mode only).</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When the row was last changed (edit mode only).</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>The owning tenant, or null for a system-blueprint row (redesign spec §3.7).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>The owner label: "System" or the tenant's name.</summary>
    public string OwnerName { get; set; } = "System";

    /// <summary>Whether this row belongs to the system blueprint.</summary>
    public bool IsSystem => TenantId is null;
}