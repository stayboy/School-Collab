using SchoolCollab.Core.Data;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// A notification template that is either tenant-owned or part of the system blueprint. One row per
/// <see cref="NotificationTemplateKind"/> x <see cref="NotificationChannel"/> per <b>scope</b>: a real
/// tenant's row (<see cref="TenantId"/> = that tenant) overrides the system row
/// (<see cref="TenantId"/> = <see langword="null"/>) for the same slot, and a missing pair means the
/// built-in defaults in the renderer apply.
/// <para>
/// <b>Hybrid tenancy (redesign spec 3.7).</b> Follows the <see cref="CodedValue"/> shape: nullable
/// <c>tenant_id</c>, the named <c>"Tenant"</c> filter
/// <c>TenantId == CurrentTenantId OR TenantId == null</c>, and <see cref="Guid.Empty"/> is never a
/// stored value - a system row is <see langword="null"/>, a tenant row is that tenant's id.
/// </para>
/// </summary>
public sealed class TenantNotificationTemplate
    : IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion, IHybridTenantEntity
{
    /// <summary>
    /// The entity-code rule that mints <see cref="Code"/> (naming spec D5). In-context: Settings owns
    /// both the rule and the row, so the create/adopt/promote handlers call the generator directly.
    /// </summary>
    public const string CodeRule = "NOTIFICATION_TEMPLATE_CODE";

    private TenantNotificationTemplate() { }

    /// <inheritdoc />
    public Guid Id { get; private set; }

    /// <summary>The notification kind this template renders.</summary>
    public NotificationTemplateKind Kind { get; private set; }

    /// <summary>The delivery channel this template renders for.</summary>
    public NotificationChannel Channel { get; private set; }

    /// <summary>
    /// The operator's own name for this template ("Grade 8 Science reminder") - the row's identity in
    /// every surface (naming spec D3). Required; never derived from the body or the subject.
    /// </summary>
    public string Title { get; private set; } = default!;

    /// <summary>
    /// Optional one-or-two-sentence note on what the template is for. Display-only metadata: it is
    /// never merged into render tags, delivered, or read by the applicability matcher (naming spec D4).
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// The generated template code (<c>TPL07</c>), minted once at creation from the
    /// <c>NOTIFICATION_TEMPLATE_CODE</c> entity-code rule (naming spec D5/D6). Never rewritten by
    /// <see cref="Update"/>; <see langword="null"/> for a row that predates the code column.
    /// </summary>
    public string? Code { get; private set; }

    /// <summary>Subject line (email only; null for SMS/WhatsApp).</summary>
    public string? Subject { get; private set; }

    /// <summary>Message body (plain text for SMS/WhatsApp; HTML for email).</summary>
    public string Body { get; private set; } = default!;

    // Hybrid tenancy (redesign spec 3.7):
    // null  = system blueprint row, visible to every tenant and edited only in system scope.
    // real  = tenant-owned row, isolated to that tenant.
    // Guid.Empty is never valid.
    /// <summary>The owning tenant, or <see langword="null"/> for a system-blueprint row.</summary>
    public Guid? TenantId { get; private set; }

    /// <inheritdoc />
    Guid? IHybridTenantEntity.TenantId
    {
        get => TenantId;
        set => TenantId = value;
    }

    /// <summary>Whether this row is part of the system blueprint (no owning tenant).</summary>
    public bool IsSystem => TenantId is null;

    /// <summary>xmin row version for optimistic concurrency.</summary>
    public uint RowVersion { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <inheritdoc />
    public bool IsDeleted { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Constraint set on this template (audience + content dimensions).</summary>
    public IReadOnlyList<TemplateConstraint> Constraints => _constraints;
    private readonly List<TemplateConstraint> _constraints = [];

    /// <summary>Template key for audit logging: "{kind}:{channel}".</summary>
    public string TemplateKey => $"{Kind}:{Channel}";

    /// <summary>
    /// Creates a new template row in <paramref name="tenantId"/>'s scope
    /// (<see langword="null"/> = system blueprint).
    /// </summary>
    /// <param name="tenantId">
    /// The owning tenant, or <see langword="null"/> for the system blueprint. <see cref="Guid.Empty"/>
    /// is rejected: it is the in-process sentinel for "no tenant selected", never a stored scope.
    /// </param>
    /// <param name="title">The operator's own name for the template. Required (naming spec D1).</param>
    /// <param name="description">Optional free-text note describing the template.</param>
    /// <param name="code">
    /// The freshly minted <c>NOTIFICATION_TEMPLATE_CODE</c>; a blank value is stored as
    /// <see langword="null"/>. There is no later write path - a code is set once, here.
    /// </param>
    /// <param name="constraints">
    /// Constraint specs (dimension + value pairs). The rows are built inside
    /// <see cref="Create"/> so each carries this template's generated id and tenant - callers
    /// cannot know <see cref="Id"/> before the row exists.
    /// </param>
    public static TenantNotificationTemplate Create(
        Guid? tenantId,
        NotificationTemplateKind kind,
        NotificationChannel channel,
        string title,
        string? description,
        string? code,
        string? subject,
        string body,
        IReadOnlyList<(TemplateConstraintDimension Dimension, string Value)>? constraints = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Template title must not be empty.", nameof(title));

        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Template body must not be empty.", nameof(body));

        if (tenantId == Guid.Empty)
            throw new ArgumentException(
                "Guid.Empty is not a valid template scope. Use null for the system blueprint or a "
                + "real tenant id for a tenant-owned row.",
                nameof(tenantId));

        var now = DateTimeOffset.UtcNow;
        var template = new TenantNotificationTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Kind = kind,
            Channel = channel,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Code = string.IsNullOrWhiteSpace(code) ? null : code.Trim(),
            // Spec 5: the subject is ignored for every plain-text channel (an SMS or WhatsApp body has
            // no subject line), so a caller-supplied subject is dropped rather than silently stored.
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
    /// Replaces the template's title, description, subject, body and constraints. Stamps
    /// <see cref="UpdatedAt"/>. Returns the previous values so the caller can write an audit row.
    /// <see cref="Code"/> is deliberately not a parameter: a generated code is set once at creation
    /// and never rewritten (naming spec D6).
    /// </summary>
    public (string? PreviousSubject, string PreviousBody, IReadOnlyList<TemplateConstraint> PreviousConstraints,
        string PreviousTitle, string? PreviousDescription) Update(
        string title,
        string? description,
        string? subject,
        string body,
        IReadOnlyList<TemplateConstraint> constraints)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Template title must not be empty.", nameof(title));

        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Template body must not be empty.", nameof(body));

        var previous = (
            PreviousSubject: Subject,
            PreviousBody: Body,
            PreviousConstraints: (IReadOnlyList<TemplateConstraint>)_constraints.ToList(),
            PreviousTitle: Title,
            PreviousDescription: Description);

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        // Spec 5: the subject is ignored for SMS - only an email template carries one.
        Subject = Channel == NotificationChannel.Email ? subject : null;
        Body = body;
        _constraints.Clear();
        foreach (var c in constraints)
            _constraints.Add(c);

        UpdatedAt = DateTimeOffset.UtcNow;
        return previous;
    }

    /// <summary>Soft-deletes the row (spec 3.10 "Reset deletes the row"). Idempotent.</summary>
    public void MarkAsDeleted()
    {
        if (IsDeleted)
            return;

        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}