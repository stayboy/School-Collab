using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Data.Repositories;

internal sealed class CodedValueRepository(CodedValuesDbContext db) : ICodedValueRepository
{
    public Task<CodedValue?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.CodedValues
            .Include(x => x.Attributes)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<CodedValue?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        db.CodedValues
            .Include(x => x.Attributes)
            .SingleOrDefaultAsync(x => x.Code == code.Trim().ToUpperInvariant(), cancellationToken);

    public Task<CodedValue?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.CodedValues
            .IgnoreQueryFilters()
            .Include(x => x.Attributes)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        db.CodedValues.AnyAsync(x => x.Code == code.Trim().ToUpperInvariant(), cancellationToken);

    public async Task AddAsync(CodedValue codedValue, CancellationToken cancellationToken = default)
    {
        await db.CodedValues.AddAsync(codedValue, cancellationToken);
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
            cv.DeletedAt)).ToArray();
    }
}
