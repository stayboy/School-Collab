using Microsoft.EntityFrameworkCore;

namespace SchoolCollab.Core.Data.Repositories;

/// <summary>
/// Generic repository base for CRUD operations against a module <see cref="DbContext"/>.
/// Derive from this class to eliminate boilerplate Get/Add/Update/Delete implementations.
/// </summary>
/// <typeparam name="TEntity">The aggregate root type. Must implement <see cref="IEntity"/>.</typeparam>
/// <typeparam name="TContext">The concrete module DbContext type.</typeparam>
public abstract class RepositoryBase<TEntity, TContext>
    where TEntity : class, IEntity
    where TContext : ModuleDbContext
{
    protected readonly TContext Db;

    protected RepositoryBase(TContext db)
    {
        Db = db;
    }

    /// <summary>
    /// The <see cref="DbSet{TEntity}"/> used by this repository. Override if the entity
    /// is exposed through a named property on the DbContext instead of <see cref="DbContext.Set{TEntity}"/>.
    /// </summary>
    protected virtual DbSet<TEntity> Set => Db.Set<TEntity>();

    public virtual Task<TEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await Set.AddAsync(entity, cancellationToken);
        await Db.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await Db.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Set.Remove(entity);
        await Db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Generic repository base for soft-deletable aggregate roots. Extends <see cref="RepositoryBase{TEntity, TContext}"/>
/// with helpers that bypass only the "SoftDelete" named global query filter, preserving tenant isolation.
/// </summary>
/// <typeparam name="TEntity">The soft-deletable aggregate root type.</typeparam>
/// <typeparam name="TContext">The concrete module DbContext type.</typeparam>
public abstract class SoftDeletableRepositoryBase<TEntity, TContext> : RepositoryBase<TEntity, TContext>
    where TEntity : class, IEntity, ISoftDeletableEntity
    where TContext : ModuleDbContext
{
    protected SoftDeletableRepositoryBase(TContext db)
        : base(db)
    {
    }

    /// <summary>
    /// Looks up an entity by id including soft-deleted rows, while keeping the tenant filter active.
    /// </summary>
    public virtual Task<TEntity?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.IgnoreQueryFilters(["SoftDelete"]).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    /// <summary>
    /// Queryable over soft-deleted entities only. The tenant filter remains active.
    /// </summary>
    protected IQueryable<TEntity> DeletedQuery =>
        Set.IgnoreQueryFilters(["SoftDelete"]).Where(x => x.IsDeleted);
}
