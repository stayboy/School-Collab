using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Queries.GetCodedValuesByIds;

public sealed class GetCodedValuesByIdsHandler(CodedValuesDbContext db)
    : IQueryHandler<GetCodedValuesByIds, CodedValueDto[]>
{
    public async Task<CodedValueDto[]> HandleAsync(
        GetCodedValuesByIds query,
        CancellationToken cancellationToken = default)
    {
        if (query.Ids.Length == 0)
        {
            return [];
        }

        var results = await db.CodedValues
            .AsNoTracking()
            .Where(x => query.Ids.Contains(x.Id))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToArrayAsync(cancellationToken);

        return results.Select(cv => new CodedValueDto(
            cv.Id,
            cv.Code,
            cv.Name,
            cv.Description,
            cv.ParentId,
            cv.IsDisabled,
            cv.DisplayOrder,
            cv.CreatedAt,
            cv.UpdatedAt,
            cv.Attributes.Select(a => new CodedValueAttributeDto(a.Key, a.Value)).ToArray(),
            cv.AttributeDefinitions.Select(d => new CodedValueAttributeDefinitionDto(d.Key, d.DisplayName, d.DataType, d.SourceCode, d.IsRequired)).ToArray())).ToArray();
    }
}
