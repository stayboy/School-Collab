using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.CreateEntityCodeRule;

public sealed class CreateEntityCodeRuleHandler(
    IEntityCodeRuleRepository repository,
    IIntegrationEventPublisher publisher,
    ITenantProvider tenantProvider,
    ILogger<CreateEntityCodeRuleHandler> logger) : ICommandHandler<CreateEntityCodeRule, Guid>
{
    public async Task<Guid> HandleAsync(CreateEntityCodeRule command, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetActiveByCodeAsync(command.Code, cancellationToken);
        if (existing is not null)
            throw new EntityCodeRuleCodeConflictException(command.Code, existing.Id);

        var rule = EntityCodeRule.Create(command.Code, command.Name, command.Description, command.IsActive);

        // Default to a shared blueprint (TenantId = null) unless the caller is
        // explicitly creating a tenant-owned rule.
        var currentTenantId = tenantProvider.GetTenantContext().TenantId;
        rule.SetTenant(currentTenantId == Guid.Empty ? (Guid?)null : currentTenantId);

        var segments = command.Segments.Select(s => s.Type == SegmentType.Fixed
            ? EntityCodeSegment.Fixed(s.Index, s.Role, s.FixedText ?? string.Empty, s.Suffix ?? string.Empty)
            : EntityCodeSegment.Sequence(s.Index, s.Role, s.Type, s.Prefix ?? string.Empty, s.Suffix ?? string.Empty, s.ResetPeriod, s.MinWidth, s.UpperLimit))
            .ToList();
        rule.ReplaceSegments(segments);

        await repository.AddAsync(rule, cancellationToken);

        logger.LogInformation("EntityCodeRule {Code} created with id {Id}", rule.Code, rule.Id);
        return rule.Id;
    }
}

/// <summary>Thrown when creating a rule whose <c>Code</c> already exists.</summary>
public sealed class EntityCodeRuleCodeConflictException : Domain.Exceptions.DomainException
{
    public string RuleCode { get; }
    public Guid ExistingRuleId { get; }

    public EntityCodeRuleCodeConflictException(string ruleCode, Guid existingRuleId)
        : base($"Entity code rule '{ruleCode}' already exists (id {existingRuleId}).")
    {
        RuleCode = ruleCode;
        ExistingRuleId = existingRuleId;
    }
}