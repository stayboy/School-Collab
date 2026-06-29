namespace SchoolCollab.Core.CQRS;

/// <summary>
/// Handles a query and returns its result.
/// </summary>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
    where TResult : class?
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
