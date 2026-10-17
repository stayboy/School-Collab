using SchoolCollab.Core.Data;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Per-tenant, user-editable notification template. One row per tenant per
/// <see cref="NotificationTemplateKind"/> × <see cref="NotificationChannel"/>. A missing
/// row means the built-in default applies. Follows the Global-blueprint → Tenant-override
/// pattern: the template store IS the tenant override layer (there is no separate
/// system-wide blueprint row — built-in defaults live in the renderer).
/// </summary>
public sealed class TenantNotificationTemplate : BaseTenantEntityWithAudit, IHasRowVersion
{
    private TenantNotificationTemplate() { }

    /// <summary>The notification kind this template renders.</summary>
    public NotificationTemplateKind Kind { get; private set; }

    /// <summary>The delivery channel this template renders for.</summary>
    public NotificationChannel Channel { get; private set; }

    /// <summary>Subject line (email only; null for SMS/WhatsApp).</summary>
    public string? Subject { get; private set; }

    /// <summary>Message body (plain text for SMS/WhatsApp; HTML for email).</summary>
    public string Body { get; private set; } = default!;

    /// <summary>xmin row version for optimistic concurrency.</summary>
    public uint RowVersion { get; private set; }

    /// <summary>Constraint set on this template (audience + content dimensions).</summary>
    public IReadOnlyList<TemplateConstraint> Constraints => _constraints;
    private readonly List<TemplateConstraint> _constraints = [];

    /// <summary>Template key for audit logging: "{kind}:{channel}".</summary>
    public string TemplateKey => $"{Kind}:{Channel}";

    /// <summary>Creates a new template row for <paramref name="tenantId"/>.</summary>
    /// <param name="constraints">
    /// Constraint specs (dimension + value pairs). The rows are built inside
    /// <see cref="Create"/> so each carries this template's generated id — callers
    /// cannot know <see cref="IEntity.Id"/> before the row exists.
    /// </param>
    public static TenantNotificationTemplate Create(
        Guid tenantId,
        NotificationTemplateKind kind,
        NotificationChannel channel,
        string? subject,
        string body,
        IReadOnlyList<(TemplateConstraintDimension Dimension, string Value)>? constraints = null)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Template body must not be empty.", nameof(body));

        // WhatsApp delivers the SMS body (spec §2), so a WhatsApp template row
        // would be a second, silently-ignored copy of the same text. Only Email
        // and SMS are valid template channels.
        if (channel == NotificationChannel.WhatsApp)
            throw new ArgumentOutOfRangeException(
                nameof(channel), channel,
                "Notification templates are stored per Email and SMS; WhatsApp recipients use the SMS body.");

        var now = DateTimeOffset.UtcNow;
        var template = new TenantNotificationTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Kind = kind,
            Channel = channel,
            // Spec §5: the subject is ignored for SMS (an SMS body has no subject line),
            // so a caller-supplied subject is dropped rather than silently stored.
            Subject = channel == NotificationChannel.Email ? subject : null,
            Body = body,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (constraints is not null)
        {
            foreach (var (dimension, value) in constraints)
                template._constraints.Add(TemplateConstraint.Create(tenantId, template.Id, dimension, value));
        }

        return template;
    }

    /// <summary>
    /// Replaces the template's subject, body and constraints. Stamps <see cref="UpdatedAt"/>.
    /// Returns the previous values so the caller can write an audit row.
    /// </summary>
    public (string? PreviousSubject, string PreviousBody, IReadOnlyList<TemplateConstraint> PreviousConstraints) Update(
        string? subject,
        string body,
        IReadOnlyList<TemplateConstraint> constraints)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Template body must not be empty.", nameof(body));

        var previous = (Subject, Body, _constraints.ToList());

        // Spec §5: the subject is ignored for SMS — only an email template carries one.
        Subject = Channel == NotificationChannel.Email ? subject : null;
        Body = body;
        _constraints.Clear();
        foreach (var c in constraints)
            _constraints.Add(c);

        UpdatedAt = DateTimeOffset.UtcNow;
        return previous;
    }
}