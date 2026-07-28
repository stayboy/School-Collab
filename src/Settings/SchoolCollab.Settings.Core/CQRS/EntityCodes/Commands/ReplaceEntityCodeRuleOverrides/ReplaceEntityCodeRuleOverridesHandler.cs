using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.ReplaceEntityCodeRuleOverrides;

public sealed class ReplaceEntityCodeRuleOverridesHandler(
    IEntityCodeRuleRepository ruleRepository,
    ITenantEntityCodeRuleOverrideRepository overrideRepository,
    ITenantProvider tenantProvider,
    ILogger<ReplaceEntityCodeRuleOverridesHandler> logger) : ICommandHandler<ReplaceEntityCodeRuleOverrides>
{
    public async Task HandleAsync(ReplaceEntityCodeRuleOverrides command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Overrides);

        // The rule must exist (we allow overrides on both shared-blueprint and
        // tenant-owned rules — the generator skips overrides for tenant-owned
        // active rules, but the override table is still addressable so a tenant
        // can pre-stage overrides before switching their own rule back to the
        // shared blueprint).
        var rule = await ruleRepository.GetByIdAsync(command.GenerationRuleId, cancellationToken)
            ?? throw new EntityCodeRuleNotFoundException(command.GenerationRuleId);

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException(
                "Cannot manage entity-code-rule overrides without a resolved tenant context. " +
                "Override CRUD requires an authenticated admin or worker principal carrying a tenant id.");

        // Build the domain entities. The repository will reuse existing rows
        // whose Id matches and insert new ones.
        var built = new List<TenantEntityCodeRuleOverride>(command.Overrides.Count);
        foreach (var input in command.Overrides)
        {
            if (input.EntityCodeSegmentId == Guid.Empty)
                throw new ArgumentException(
                    $"Override row carries an empty EntityCodeSegmentId (field={input.Field}).",
                    nameof(command));
            if (string.IsNullOrWhiteSpace(input.Value))
                throw new ArgumentException(
                    $"Override value is required (segmentId={input.EntityCodeSegmentId}, field={input.Field}).",
                    nameof(command));
            if (!Enum.IsDefined(typeof(OverrideField), input.Field))
                throw new ArgumentException(
                    $"Unknown OverrideField value {input.Field}.",
                    nameof(command));

            // For existing rows (Id != Guid.Empty), use Rehydrate so the
            // repository's "match by id" path treats it as an update.
            built.Add(input.Id == Guid.Empty
                ? TenantEntityCodeRuleOverride.Create(tenantId, rule.Id, input.EntityCodeSegmentId, (OverrideField)input.Field, input.Value)
                : TenantEntityCodeRuleOverride.Rehydrate(input.Id, tenantId, rule.Id, input.EntityCodeSegmentId, (OverrideField)input.Field, input.Value));
        }

        await overrideRepository.ReplaceForRuleAsync(rule.Id, built, cancellationToken);

        logger.LogInformation(
            "Replaced {Count} entity-code-rule overrides for tenant {TenantId} on rule {RuleId}",
            built.Count, tenantId, rule.Id);
    }

}
