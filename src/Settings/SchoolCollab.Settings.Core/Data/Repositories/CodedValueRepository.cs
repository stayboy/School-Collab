using Microsoft.EntityFrameworkCore;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.Data.Repositories;

internal sealed class CodedValueRepository(SettingsDbContext db) : ICodedValueRepository
{
    public Task<CodedValue?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.CodedValues
            .Include(x => x.Attributes)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<CodedValue?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        db.CodedValues
            .Include(x => x.Attributes)
            .FirstOrDefaultAsync(x => x.Code == code.Trim().ToUpperInvariant(), cancellationToken);

    public Task<CodedValue?> GetByCodeAndParentAsync(string code, Guid? parentId, CancellationToken cancellationToken = default)
    {
        var normalisedCode = code.Trim().ToUpperInvariant();
        return parentId.HasValue
            ? db.CodedValues.Include(x => x.Attributes).FirstOrDefaultAsync(x => x.Code == normalisedCode && x.ParentId == parentId, cancellationToken)
            : db.CodedValues.Include(x => x.Attributes).FirstOrDefaultAsync(x => x.Code == normalisedCode && x.ParentId == null, cancellationToken);
    }

    public Task<CodedValue?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.CodedValues
            .IgnoreQueryFilters()
            .Include(x => x.Attributes)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        db.CodedValues.AnyAsync(x => x.Code == code.Trim().ToUpperInvariant(), cancellationToken);

    public Task<bool> ExistsByCodeInParentAsync(string code, Guid? parentId, CancellationToken cancellationToken = default)
    {
        var normalisedCode = code.Trim().ToUpperInvariant();
        return parentId.HasValue
            ? db.CodedValues.AnyAsync(x => x.Code == normalisedCode && x.ParentId == parentId, cancellationToken)
            : db.CodedValues.AnyAsync(x => x.Code == normalisedCode && x.ParentId == null, cancellationToken);
    }

    public Task<CodedValue?> FindConflictingByCodeAndParentAsync(
        string code,
        Guid? parentId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var normalisedCode = code.Trim().ToUpperInvariant();
        // Ignore the "Tenant" filter so shared-blueprint (NULL) rows AND any
        // tenant-owned rows are visible. The soft-delete filter is retained (a
        // deleted row is not a conflict). Match the existing (parent, code) pair;
        // prefer a shared-blueprint row when both shared and owned exist so the
        // guard directs the tenant to override the shared name (FR-6).
        var candidates = db.CodedValues
            .IgnoreQueryFilters(["Tenant"])
            .Where(x => x.Code == normalisedCode
                && (parentId.HasValue ? x.ParentId == parentId : x.ParentId == null)
                && (x.TenantId == null || x.TenantId == tenantId));
        return candidates
            .OrderBy(x => x.TenantId == null ? 0 : 1) // shared (NULL) first
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(CodedValue codedValue, CancellationToken cancellationToken = default)
    {
        await db.CodedValues.AddAsync(codedValue, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<CodedValue> codedValues, CancellationToken cancellationToken = default)
    {
        await db.CodedValues.AddRangeAsync(codedValues, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(CodedValue codedValue, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(codedValue.Id);
        }
    }

    public async Task<int> CountChildrenAsync(Guid parentId, CancellationToken cancellationToken = default) =>
        await db.CodedValues.CountAsync(x => x.ParentId == parentId, cancellationToken);

    public async Task<List<string>> GetReferencingSourceCodesAsync(Guid codedValueId, CancellationToken cancellationToken = default)
    {
        var code = await db.CodedValues
            .Where(x => x.Id == codedValueId)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (code is null) return [];

        return await db.CodedValues
            .AsNoTracking()
            .Where(x => x.AttributeDefinitions.Any(d => d.SourceCode == code))
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<CodedValueDto[]> ListDeletedAsync(CancellationToken cancellationToken = default)
    {
        var results = await db.CodedValues
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IsDeleted)
            .OrderBy(x => x.Name)
            .ToArrayAsync(cancellationToken);

        return results.Select(cv => new CodedValueDto(
            cv.Id,
            cv.Code,
            cv.Name,
            cv.Description,
            cv.ParentId,
            (string?)null,
            cv.IsDisabled,
            cv.DisplayOrder,
            cv.CreatedAt,
            cv.UpdatedAt,
            cv.Attributes.Select(a => new CodedValueAttributeDto(a.Key, a.Value)).ToArray(),
            cv.AttributeDefinitions.Select(d => new CodedValueAttributeDefinitionDto(d.Key, d.DisplayName, d.DataType, d.SourceCode, d.IsRequired, d.AllowMultiple, d.MinLength, d.MaxLength, d.RegexPattern)).ToArray(),
            0,
            cv.IsDeleted,
            cv.DeletedAt,
            false,
            cv.Name)).ToArray(); // No override applied here; DefaultName == Name
    }

    public async Task<TenantCodedValueOverride?> GetOverrideAsync(Guid tenantId, Guid codedValueId, CancellationToken cancellationToken = default) =>
        await db.TenantCodedValueOverrides
            .IgnoreQueryFilters(["Tenant"])
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.GlobalCodedValueId == codedValueId, cancellationToken);

    public async Task UpsertOverrideAsync(TenantCodedValueOverride overrideValue, CancellationToken cancellationToken = default)
    {
        var existing = await GetOverrideAsync(overrideValue.TenantId, overrideValue.GlobalCodedValueId, cancellationToken);
        if (existing is not null)
        {
            db.TenantCodedValueOverrides.Remove(existing);
        }
        await db.TenantCodedValueOverrides.AddAsync(overrideValue, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveOverrideAsync(Guid tenantId, Guid codedValueId, CancellationToken cancellationToken = default)
    {
        var existing = await GetOverrideAsync(tenantId, codedValueId, cancellationToken);
        if (existing is not null)
        {
            db.TenantCodedValueOverrides.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<TenantCodedValueAttributeOverride?> GetAttributeOverrideAsync(Guid tenantId, Guid codedValueId, string key, CancellationToken cancellationToken = default) =>
        await db.TenantCodedValueAttributeOverrides
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.GlobalCodedValueId == codedValueId && x.AttributeKey == key, cancellationToken);

    public async Task UpsertAttributeOverrideAsync(TenantCodedValueAttributeOverride attributeOverride, CancellationToken cancellationToken = default)
    {
        var existing = await GetAttributeOverrideAsync(attributeOverride.TenantId, attributeOverride.GlobalCodedValueId, attributeOverride.AttributeKey, cancellationToken);
        if (existing is not null)
        {
            db.TenantCodedValueAttributeOverrides.Remove(existing);
        }
        await db.TenantCodedValueAttributeOverrides.AddAsync(attributeOverride, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAttributeOverrideAsync(Guid tenantId, Guid codedValueId, string key, CancellationToken cancellationToken = default)
    {
        var existing = await GetAttributeOverrideAsync(tenantId, codedValueId, key, cancellationToken);
        if (existing is not null)
        {
            db.TenantCodedValueAttributeOverrides.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
