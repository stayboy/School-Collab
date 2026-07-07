using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueById;

/// <summary>
/// Resolves a global coded value for the current tenant, applying any
/// <c>TenantCodedValueOverride</c> to produce the display name and
/// description the caller should see.
/// </summary>
/// <remarks>
/// <b>Why no cache?</b> Earlier versions of this handler wrapped the read
/// in <c>HybridCache</c> (key <c>coded-value:{id}</c>, tag
/// <c>coded-values</c>). The <c>DELETE_Override_RevertsToBlueprintName</c>
/// integration test in <c>tests/SchoolCollab.Settings.Tests.Integration</c>
/// revealed that tag-based and key-based invalidation were not reliably
/// clearing the L1 in-memory layer in the test environment, so the next
/// read after a PUT/DELETE override would return the stale (pre-write)
/// value. The handler is a single indexed lookup on the coded value plus
/// a single lookup on the override table — fast enough to not need a
/// cache. If a cache is reintroduced later, it must be verified to
/// invalidate correctly on every write path (PUT, DELETE) for both
/// real-tenant and default-tenant branches.
/// </remarks>
public sealed class GetCodedValueByIdHandler(
    SettingsDbContext db) : IQueryHandler<GetCodedValueById, CodedValueDto?>
{
    public async Task<CodedValueDto?> HandleAsync(
        GetCodedValueById query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        var cv = await db.CodedValues
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == query.Id, cancellationToken);

        if (cv is null)
            return null;

        var overrideVal = await db.TenantCodedValueOverrides
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.GlobalCodedValueId == query.Id && x.TenantId == tenantId, cancellationToken);

        string? parentCode = cv.ParentId.HasValue
            ? await db.CodedValues.AsNoTracking()
                .Where(x => x.Id == cv.ParentId.Value)
                .Select(x => x.Code)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return new CodedValueDto(
            cv.Id,
            cv.Code,
            overrideVal?.OverriddenName ?? cv.Name,
            overrideVal?.OverriddenDescription ?? cv.Description,
            cv.ParentId,
            parentCode,
            cv.IsDisabled,
            cv.DisplayOrder,
            cv.CreatedAt,
            cv.UpdatedAt,
            cv.Attributes.Select(a => new CodedValueAttributeDto(a.Key, a.Value)).ToArray(),
            cv.AttributeDefinitions.Select(d => new CodedValueAttributeDefinitionDto(d.Key, d.DisplayName, d.DataType, d.SourceCode, d.IsRequired, d.AllowMultiple, d.MinLength, d.MaxLength, d.RegexPattern)).ToArray(),
            0,
            cv.IsDeleted,
            cv.DeletedAt,
            overrideVal is not null);
    }
}
