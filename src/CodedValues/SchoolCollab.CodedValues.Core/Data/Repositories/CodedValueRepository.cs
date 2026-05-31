using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Core.Data.Repositories;

internal sealed class CodedValueRepository(CodedValuesDbContext db) : ICodedValueRepository
{
    public Task<CodedValue?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.CodedValues
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
}
