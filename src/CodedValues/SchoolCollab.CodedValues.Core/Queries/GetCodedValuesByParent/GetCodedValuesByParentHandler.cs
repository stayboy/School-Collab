using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Queries.GetCodedValuesByParent;

public sealed class GetCodedValuesByParentHandler(CodedValuesDbContext db)
    : IQueryHandler<GetCodedValuesByParent, CodedValueDto[]>
{
    public async Task<CodedValueDto[]> HandleAsync(
        GetCodedValuesByParent query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Domain.CodedValue> q = db.CodedValues
            .AsNoTracking();

        if (!query.IncludeDisabled)
        {
            q = q.Where(x => !x.IsDisabled);
        }

        if (query.ParentId.HasValue)
        {
            q = q.Where(x => x.ParentId == query.ParentId);
        }
        else if (!string.IsNullOrWhiteSpace(query.ParentCode))
        {
            var parentCode = query.ParentCode.Trim().ToUpperInvariant();
            var parentId = await db.CodedValues
                .AsNoTracking()
                .Where(x => x.Code == parentCode)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);

            q = q.Where(x => x.ParentId == parentId);
        }

        if (query.AttributeFilters is { Count: > 0 })
        {
            foreach (var (key, value) in query.AttributeFilters)
            {
                var k = key;
                var v = value;
                q = q.Where(x => x.Attributes.Any(a => a.Key == k && a.Value == v));
            }
        }

        var results = await q
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToArrayAsync(cancellationToken);

        return results.Select(ToDto).ToArray();
    }

    private static CodedValueDto ToDto(Domain.CodedValue cv) => new(
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
        cv.AttributeDefinitions.Select(d => new CodedValueAttributeDefinitionDto(d.Key, d.DisplayName, d.DataType, d.SourceCode, d.IsRequired, d.AllowMultiple)).ToArray());
}
