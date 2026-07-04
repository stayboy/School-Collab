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
    Task AddAsync(CodedValue codedValue, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<CodedValue> codedValues, CancellationToken cancellationToken = default);
    Task UpdateAsync(CodedValue codedValue, CancellationToken cancellationToken = default);
    Task<int> CountChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);
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
