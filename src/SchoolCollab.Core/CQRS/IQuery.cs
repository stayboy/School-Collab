namespace SchoolCollab.Core.CQRS;

/// <summary>
/// Marker interface for a query. Queries are read-only operations handled by
/// <see cref="IQueryHandler{TQuery, TResult}"/> and must not mutate state.
/// </summary>
public interface IQuery<TResult>
    where TResult : class?
{
}
