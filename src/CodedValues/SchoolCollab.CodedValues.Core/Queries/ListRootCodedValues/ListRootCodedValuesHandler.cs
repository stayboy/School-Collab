using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Queries.ListRootCodedValues;

public sealed class ListRootCodedValuesHandler(CodedValuesDbContext db)
    : IQueryHandler<ListRootCodedValues, CodedValueDto[]>
{
    public async Task<CodedValueDto[]> HandleAsync(
        ListRootCodedValues query,
        CancellationToken cancellationToken = default)
    {
        var results = await db.CodedValues
            .AsNoTracking()
            .Include(x => x.Attributes)
            .Where(x => x.ParentId == null)
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
            cv.Attributes.ToDictionary(a => a.Key, a => a.Value))).ToArray();
    }
}
