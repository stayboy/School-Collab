using SchoolCollab.Core.Data;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// A signed-in operator's personal default tenant for the admin portal — the tenant
/// the portal applies on load when that operator has expressed no explicit selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Global by design.</b> This row is a preference <i>about</i> a tenant, not data
/// <i>belonging to</i> one, so the entity implements neither <see cref="ITenantEntity"/>
/// nor <see cref="IHybridTenantEntity"/> and carries no global query filter: it is
/// listed in <c>SettingsDbContext.GlobalEntityAllowList</c>. <see cref="DefaultTenantId"/>
/// is therefore a value, not a scope — exactly like <c>Tenant</c> itself.
/// </para>
/// <para>
/// <b>Ownership is by subject, not by row.</b> A preference belongs to whoever the
/// identity provider says is signed in, so the natural key is the OIDC <c>sub</c> claim
/// (<see cref="SubjectId"/>) rather than a foreign key to the settings-local
/// <c>User</c> lookup table — which is never populated and would drag a whole
/// identity-sync lifecycle into scope. Under <c>TestAuthHandler</c> the subject is the
/// synthetic <c>test-user-id</c>, so the preference works in dev without Keycloak.
/// </para>
/// <para>
/// <see cref="DefaultTenantId"/> is nullable and the two "empty" values mean different
/// things, mirroring <c>IDevTenantSelection</c>: <see langword="null"/> = <i>no
/// preference recorded</i> (fall through to the platform default), while
/// <see cref="Guid.Empty"/> = <i>an explicit preference for the system / no-tenant
/// context</i> — the state shared-blueprint coded-value editing needs.
/// </para>
/// </remarks>
public sealed class AdminUserPreference : IEntity, IAuditableEntity
{
    /// <summary>Longest accepted subject identifier. OIDC subjects are UUIDs; the headroom is defensive.</summary>
    public const int MaxSubjectIdLength = 200;

    private AdminUserPreference() { }

    public Guid Id { get; private set; }

    /// <summary>The identity provider's subject identifier (<c>sub</c> claim) this preference belongs to.</summary>
    public string SubjectId { get; private set; } = default!;

    /// <summary>
    /// The operator's preferred tenant. <see langword="null"/> = no preference recorded;
    /// <see cref="Guid.Empty"/> = an explicit preference for the system / no-tenant context.
    /// </summary>
    public Guid? DefaultTenantId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a preference row for <paramref name="subjectId"/> with a fresh
    /// <see cref="Id"/>. The caller supplies the tenant; it is not validated against the
    /// tenant registry here (the handler does that, where the registry is in scope).
    /// </summary>
    public static AdminUserPreference Create(string subjectId, Guid? defaultTenantId)
    {
        var subject = NormaliseSubject(subjectId);

        var now = DateTimeOffset.UtcNow;
        return new AdminUserPreference
        {
            Id = Guid.NewGuid(),
            SubjectId = subject,
            DefaultTenantId = defaultTenantId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Replaces the preferred tenant. Passing <see cref="Guid.Empty"/> records an explicit
    /// preference for the system / no-tenant context; passing <see langword="null"/> clears
    /// the preference so the platform default applies again.
    /// </summary>
    public void SetDefaultTenant(Guid? tenantId)
    {
        if (DefaultTenantId == tenantId)
            return;

        DefaultTenantId = tenantId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string NormaliseSubject(string subjectId)
    {
        var trimmed = subjectId?.Trim()
            ?? throw new ArgumentNullException(nameof(subjectId));

        if (trimmed.Length is 0)
            throw new ArgumentException("Subject id must not be empty.", nameof(subjectId));

        if (trimmed.Length > MaxSubjectIdLength)
            throw new ArgumentOutOfRangeException(nameof(subjectId),
                $"Subject id must be at most {MaxSubjectIdLength} characters.");

        return trimmed;
    }
}
