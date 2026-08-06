using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.Services;

public interface ICodedValueResolver
{
    Task<CodedValueDto> ResolveAsync(CodedValue cv, Guid tenantId, CancellationToken ct = default);
}

public sealed class CodedValueResolver(ICodedValueRepository repository) : ICodedValueResolver
{
    public async Task<CodedValueDto> ResolveAsync(CodedValue cv, Guid tenantId, CancellationToken ct = default)
    {
        // 1. Resolve Basic Properties (Code, Name, Description)
        var overrideValue = await repository.GetOverrideAsync(tenantId, cv.Id, ct);

        string finalCode = overrideValue?.OverriddenCode ?? cv.Code; // Code is now tenant-overridable (tcv/1); falls back to global blueprint
        string finalName = overrideValue?.OverriddenName ?? cv.Name;
        string? finalDescription = overrideValue?.OverriddenDescription ?? cv.Description;
        bool isOverridden = overrideValue is not null;

        // 2. Resolve Attributes (Child attributes)
        var resolvedAttributes = new List<CodedValueAttributeDto>();
        foreach (var attr in cv.Attributes)
        {
            var attrOverride = await repository.GetAttributeOverrideAsync(tenantId, cv.Id, attr.Key, ct);
            resolvedAttributes.Add(new CodedValueAttributeDto(
                attr.Key, 
                attrOverride?.CustomValue ?? attr.Value));
        }

        // 3. Resolve Parent Code (Global metadata)
        string? parentCode = cv.ParentId.HasValue
            ? (await repository.GetAsync(cv.ParentId.Value, ct))?.Code
            : null;

        return new CodedValueDto(
            cv.Id,
            finalCode,
            finalName,
            finalDescription,
            cv.ParentId,
            parentCode,
            cv.IsDisabled,
            cv.DisplayOrder,
            cv.CreatedAt,
            cv.UpdatedAt,
            resolvedAttributes.ToArray(),
            cv.AttributeDefinitions.Select(d => new CodedValueAttributeDefinitionDto(
                d.Key, d.DisplayName, d.DataType, d.SourceCode, d.IsRequired, d.AllowMultiple, d.MinLength, d.MaxLength, d.RegexPattern)).ToArray(),
            0,
            cv.IsDeleted,
            cv.DeletedAt,
            isOverridden,
            cv.Name, // DefaultName is the global name (before tenant override)
            cv.Code); // DefaultCode is the global code (before tenant override)
    }
}
