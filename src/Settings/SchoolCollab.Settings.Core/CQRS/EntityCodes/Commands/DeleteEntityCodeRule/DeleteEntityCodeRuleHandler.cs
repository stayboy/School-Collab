using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.DeleteEntityCodeRule;

public sealed class DeleteEntityCodeRuleHandler(
    IEntityCodeRuleRepository repository,
    ILogger<DeleteEntityCodeRuleHandler> logger) : ICommandHandler<DeleteEntityCodeRule>
{
    public async Task HandleAsync(DeleteEntityCodeRule command, CancellationToken cancellationToken = default)
    {
        var rule = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new EntityCodeRuleNotFoundException(command.Id);

        rule.Delete();
        await repository.UpdateAsync(rule, cancellationToken);

        logger.LogInformation("EntityCodeRule {Id} soft-deleted", command.Id);
    }
}