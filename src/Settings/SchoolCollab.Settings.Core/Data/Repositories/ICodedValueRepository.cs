using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.Data.Repositories;

public interface ICodedValueRepository
{
    Task<CodedValue?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CodedValue?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<CodedValue?> GetByCodeAndParentAsync(string code, Guid? parentId, CancellationToken cancellationToken = default);
    Task<CodedValue?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeInParentAsync(string code, Guid? parentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Duplicate-code guard lookup (FR-6 / §3.4). Returns the existing coded value
    /// with the same normalized <paramref name="code"/> and <paramref name="parentId"/>
    /// in the tenant-visible scope — <b>ignoring the "Tenant" query filter</b> so both
    /// shared-blueprint (<c>NULL</c>) rows and the current tenant's owned rows are
    /// considered. <c>tenantId</c> scopes the owned-row check (pass the current
    /// tenant; pass <see langword="null"/> for the default/dev blueprint path which
    /// only checks shared rows). Returns <see langword="null"/> if no conflict.
    /// </summary>
    Task<CodedValue?> FindConflictingByCodeAndParentAsync(
        string code,
        Guid? parentId,
        Guid? tenantId,
        CancellationToken cancellationToken = default);
    Task AddAsync(CodedValue codedValue, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<CodedValue> codedValues, CancellationToken cancellationToken = default);
    Task UpdateAsync(CodedValue codedValue, CancellationToken cancellationToken = default);
    Task<int> CountChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a sibling coded value under the given parent with the specified
    /// <paramref name="displayOrder"/>. Returns <see langword="null"/> if no
    /// sibling matches. Used for grade-level DisplayOrder uniqueness.
    /// </summary>
    Task<CodedValue?> FindSiblingByDisplayOrderAsync(Guid parentId, int displayOrder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a sibling coded value under the given parent that has a
    /// <c>gradeLevel</c> attribute with <paramref name="gradeLevelValue"/>
    /// AND a <c>strandVersion</c> attribute with
    /// <paramref name="strandVersionValue"/>. Returns <see langword="null"/>
    /// if no sibling matches. Used for per-grade strand uniqueness.
    /// </summary>
    Task<CodedValue?> FindStrandSiblingAsync(
        Guid parentId,
        string gradeLevelValue,
        string strandVersionValue,
        CancellationToken cancellationToken = default);
    Task<List<string>> GetReferencingSourceCodesAsync(Guid codedValueId, CancellationToken cancellationToken = default);
    Task<CodedValueDto[]> ListDeletedAsync(CancellationToken cancellationToken = default);

    // Tenancy Overrides
    Task<TenantCodedValueOverride?> GetOverrideAsync(Guid tenantId, Guid codedValueId, CancellationToken cancellationToken = default);
    Task UpsertOverrideAsync(TenantCodedValueOverride overrideValue, CancellationToken cancellationToken = default);
    Task RemoveOverrideAsync(Guid tenantId, Guid codedValueId, CancellationToken cancellationToken = default);
    
    Task<TenantCodedValueAttributeOverride?> GetAttributeOverrideAsync(Guid tenantId, Guid codedValueId, string key, CancellationToken cancellationToken = default);
    Task UpsertAttributeOverrideAsync(TenantCodedValueAttributeOverride attributeOverride, CancellationToken cancellationToken = default);
    Task RemoveAttributeOverrideAsync(Guid tenantId, Guid codedValueId, string key, CancellationToken cancellationToken = default);
}
