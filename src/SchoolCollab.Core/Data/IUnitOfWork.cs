using SchoolCollab.Core.Data;

namespace SchoolCollab.Core.Data;

/// <summary>
/// A request-scoped unit of work that runs a batch of writes inside a single
/// EF Core transaction, committing once at the end. If any step throws, the
/// whole batch is rolled back — no partial commit is possible.
/// </summary>
/// <typeparam name="TContext">The concrete module <see cref="ModuleDbContext"/>.</typeparam>
/// <remarks>
/// This is an explicit, opt-in callable (not an ambient/scope thing). Compound
/// command handlers that must be atomic (e.g. create-teacher-with-assignments)
/// invoke <see cref="ExecuteAsync{TResult}"/> and build the full EF Core tracking
/// graph inside the action, calling <c>SaveChangesAsync</c> exactly once. The
/// transaction uses EF Core's <see cref="Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy"/>
/// so transient Postgres faults are retried before the transaction is abandoned.
/// </remarks>
public interface IUnitOfWork<out TContext>
    where TContext : ModuleDbContext
{
    /// <summary>
    /// Executes <paramref name="action"/> inside a single transaction with
    /// <see cref="Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy"/> retry.
    /// The action tracks entities on the context and must call
    /// <c>SaveChangesAsync</c>; the transaction is committed only if the action
    /// returns without throwing. Any exception rolls back the entire batch and
    /// propagates unchanged (so the API layer can map domain exceptions to 4xx).
    /// </summary>
    Task<TResult> ExecuteAsync<TResult>(
        Func<TContext, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default);
}
