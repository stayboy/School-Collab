namespace SchoolCollab.Core.CQRS;

/// <summary>
/// Handles a command that does not return a result beyond completion.
/// </summary>
public interface ICommandHandler<in TCommand>
    where TCommand : class, ICommand
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles a command that returns a result (e.g. the id of a created entity).
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : class, ICommand
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
