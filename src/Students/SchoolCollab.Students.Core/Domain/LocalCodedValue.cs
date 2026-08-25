namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Locally-replicated copy of a Settings coded value
/// (adr-cross-module-calls.md Phase 1). Maintained exclusively by the coded
/// value projection consumer and the one-time backfill; never written by
/// domain handlers.
///
/// <para><b>Three disjoint row kinds share this table</b> (unique per
/// (<see cref="TenantId"/>, <see cref="Id"/>)):</para>
/// <list type="bullet">
///   <item><b>Global blueprint row</b> — <c>TenantId = null</c>, mirrors a
///     shared CodedValue.</item>
///   <item><b>Tenant-owned row</b> — real tenant id, mirrors a tenant-owned
///     (e.g. provisional) CodedValue.</item>
///   <item><b>Tenant override row</b> — real tenant id (or Guid.Empty for the
///     default tenant) over a global Id; carries only the overridden display
///     fields (null = keep global value), mirroring TenantCodedValueOverride.</item>
/// </list>
///
/// <para>An override row and its global row share the same <see cref="Id"/>;
/// a tenant-owned row's Id never collides with any global Id. Resolution
/// (global + overlay merge) lives in ILocalCodedValueRepository.</para>
/// </summary>
public sealed class LocalCodedValue
{
    /// <summary>Surrogate PK — (TenantId, Id) is the logical key via unique index.</summary>
    public Guid RowId { get; set; } = Guid.NewGuid();

    public Guid Id { get; set; }

    /// <summary>null = shared blueprint; Guid.Empty = default-tenant overlay; real id = tenant-owned/override.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Nullable: overlay rows carry null for fields not overridden ("keep global").</summary>
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentCode { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsDeleted { get; set; }
    public int DisplayOrder { get; set; }
    public List<LocalCodedValueAttribute> Attributes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Key/value attribute replicated from the coded value's Attributes.</summary>
public sealed record LocalCodedValueAttribute(string Key, string Value);
