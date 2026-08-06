using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.ListProvisionalCodedValues;

/// <summary>
/// Returns provisional coded values across all tenants (tcv/3). Because provisional
/// rows are tenant-owned, the admin/default tenant context cannot see them through
/// the normal "Tenant" filter, so it is ignored here. Each row is mapped with
/// <c>IsProvisional = true</c> and its owning <c>TenantId</c> so the approval UI can
/// show who requested it.
/// </summary>
public sealed class ListProvisionalCodedValuesHandler(SettingsDbContext db)
    : IQueryHandler<ListProvisionalCodedValues, CodedValueDto[]>
{
    public async Task<CodedValueDto[]> HandleAsync(ListProvisionalCodedValues query, CancellationToken ct = default)
    {
        var rows = await db.CodedValues
            .IgnoreQueryFilters(["Tenant"])
            .AsNoTracking()
            .Where(x => x.IsProvisional && x.TenantId != null && x.TenantId != Guid.Empty)
            .OrderBy(x => x.CreatedAt)
            .ToArrayAsync(ct);

        return rows.Select(cv => new CodedValueDto(
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
            false,
            null,
            false,
            null,
            null,
            true,
            cv.TenantId)).ToArray();
    }
}
