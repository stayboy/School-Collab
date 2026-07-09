namespace SchoolCollab.Settings.Core.Domain.Exceptions;

/// <summary>
/// Thrown by <c>CreateCodedValueHandler</c> when a coded value with the same
/// <c>(parent, code)</c> already exists in the tenant-visible scope, so creating a
/// duplicate would surface two rows with the same code in a tenant's dropdown
/// (the hybrid filter surfaces both <c>NULL</c>-blueprint and tenant-owned rows).
/// </summary>
/// <remarks>
/// Per <c>documents/specs/global-tenant-filter.md</c> §3.4 / FR-6: when the
/// conflicting row is a <b>shared blueprint</b> (<c>tenant_id IS NULL</c>), the
/// caller is directed to <b>override its name</b> via the existing
/// <c>UpsertCodedValueOverride</c> (the per-row "Override Name" UX) rather than
/// creating a tenant-owned duplicate. When the conflict is the tenant's own
/// existing owned row, the caller is directed to update that row instead.
/// </remarks>
public sealed class CodedValueCodeConflictException : DomainException
{
    /// <summary>The normalized code that collided.</summary>
    public string Code { get; }

    /// <summary>The parent id of the colliding coded value (null = root).</summary>
    public Guid? ParentId { get; }

    /// <summary>The id of the existing conflicting coded value, if known.</summary>
    public Guid? ExistingCodedValueId { get; }

    /// <summary>
    /// <see langword="true"/> if the existing conflicting row is a shared blueprint
    /// (<c>tenant_id IS NULL</c>); <see langword="false"/> if it is the tenant's own
    /// owned row.
    /// </summary>
    public bool ExistingIsSharedBlueprint { get; }

    public CodedValueCodeConflictException(
        string code,
        Guid? parentId,
        Guid? existingCodedValueId,
        bool existingIsSharedBlueprint)
        : base(existingIsSharedBlueprint
            ? $"A shared blueprint coded value with code '{code}' already exists" +
              (parentId.HasValue ? $" under parent '{parentId.Value}'" : " as a root value") +
              ". Override its name via UpsertCodedValueOverride instead of creating a duplicate."
            : $"A tenant-owned coded value with code '{code}' already exists" +
              (parentId.HasValue ? $" under parent '{parentId.Value}'" : " as a root value") +
              ". Update the existing row instead of creating a duplicate.")
    {
        Code = code;
        ParentId = parentId;
        ExistingCodedValueId = existingCodedValueId;
        ExistingIsSharedBlueprint = existingIsSharedBlueprint;
    }
}
