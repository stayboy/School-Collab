using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.UpdateEntityCodeRule;

public sealed class UpdateEntityCodeRuleHandler(
    IEntityCodeRuleRepository repository,
    ILogger<UpdateEntityCodeRuleHandler> logger) : ICommandHandler<UpdateEntityCodeRule>
{
    public async Task HandleAsync(UpdateEntityCodeRule command, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters so admins can edit soft-deleted rules if needed.
        var rule = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new EntityCodeRuleNotFoundException(command.Id);

        rule.Update(command.Name, command.Description, command.IsActive);

        var segments = command.Segments.Select(s => s.Type == SegmentType.Fixed
            ? EntityCodeSegment.Fixed(s.Index, s.Role, s.FixedText ?? string.Empty, s.Suffix ?? string.Empty)
            : EntityCodeSegment.Sequence(s.Index, s.Role, s.Type, s.Prefix ?? string.Empty, s.Suffix ?? string.Empty, s.ResetPeriod, s.MinWidth, s.UpperLimit))
            .ToList();
        rule.ReplaceSegments(segments);

        await repository.UpdateAsync(rule, cancellationToken);

        logger.LogInformation("EntityCodeRule {Id} updated", rule.Id);
    }
}