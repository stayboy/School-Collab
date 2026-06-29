namespace SchoolCollab.Core.CQRS;

/// <summary>
/// Marker interface for a command. Commands are state-changing operations
/// (creates, updates, deletes) handled by <see cref="ICommandHandler{TCommand}"/>
/// or <see cref="ICommandHandler{TCommand, TResult}"/>.
/// </summary>
public interface ICommand
{
}
