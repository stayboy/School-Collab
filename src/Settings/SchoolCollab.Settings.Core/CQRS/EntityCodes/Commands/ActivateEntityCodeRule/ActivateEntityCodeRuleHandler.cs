using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.ActivateEntityCodeRule;

public sealed class ActivateEntityCodeRuleHandler(
    IEntityCodeRuleRepository repository,
    ILogger<ActivateEntityCodeRuleHandler> logger) : ICommandHandler<ActivateEntityCodeRule>
{
    public async Task HandleAsync(ActivateEntityCodeRule command, CancellationToken cancellationToken = default)
    {
        var rule = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new EntityCodeRuleNotFoundException(command.Id);

        // Deactivate any other active rule with the same Code (only one active
        // per Code is allowed; spec §3.1).
        var all = await repository.ListAsync(cancellationToken);
        foreach (var other in all)
        {
            if (other.Id == rule.Id) continue;
            if (other.Code == rule.Code && other.IsActive)
            {
                other.Deactivate();
                await repository.UpdateAsync(other, cancellationToken);
            }
        }

        rule.Activate();
        await repository.UpdateAsync(rule, cancellationToken);

        logger.LogInformation("EntityCodeRule {Id} activated", rule.Id);
    }
}