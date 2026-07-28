using Microsoft.EntityFrameworkCore;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.Data.Repositories;

internal sealed class EntityCodeRuleRepository(SettingsDbContext db) : IEntityCodeRuleRepository
{
    public Task<EntityCodeRule?> GetActiveByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalised = code.Trim().ToUpperInvariant();
        return db.EntityCodeRules
            .SingleOrDefaultAsync(x => x.Code == normalised && x.IsActive, cancellationToken);
    }

    public Task<EntityCodeRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.EntityCodeRules
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<List<EntityCodeRule>> ListAsync(CancellationToken cancellationToken = default) =>
        db.EntityCodeRules
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(EntityCodeRule rule, CancellationToken cancellationToken = default)
    {
        await db.EntityCodeRules.AddAsync(rule, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(EntityCodeRule rule, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(rule.Id);
        }
    }
}