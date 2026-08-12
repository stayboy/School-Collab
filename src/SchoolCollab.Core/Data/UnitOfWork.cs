using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SchoolCollab.Core.Data;

/// <summary>
/// Default <see cref="IUnitOfWork{TContext}"/> backed by EF Core's
/// <see cref="IExecutionStrategy"/> transaction execution, which retries on
/// transient Postgres faults (serialization/deadlock) before giving up.
/// </summary>
/// <typeparam name="TContext">The concrete module <see cref="ModuleDbContext"/>.</typeparam>
public sealed class UnitOfWork<TContext> : IUnitOfWork<TContext>
    where TContext : ModuleDbContext
{
    private readonly TContext _db;

    public UnitOfWork(TContext db)
    {
        _db = db;
    }

    public Task<TResult> ExecuteAsync<TResult>(
        Func<TContext, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return strategy.ExecuteInTransactionAsync(
            _db,
            async (db, ct) => await action((TContext)db, ct),
            (_, _) => Task.FromResult(true),
            IsolationLevel.Serializable,
            cancellationToken);
    }
}
