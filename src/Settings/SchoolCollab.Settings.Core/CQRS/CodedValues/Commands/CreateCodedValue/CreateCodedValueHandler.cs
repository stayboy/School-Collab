using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Events;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateCodedValue;

/// <summary>
/// Creates a coded value. <b>Dual-mode tenancy</b> (global-tenant-filter.md
/// §3.3 / FR-5):
/// <list type="bullet">
/// <item><b>Real tenant</b> (<c>CurrentTenantId != Guid.Empty</c>): stamps a
///   <b>tenant-owned</b> row (<c>TenantId = current</c>), isolated to that tenant.</item>
/// <item><b>Default/dev tenant</b> (<c>CurrentTenantId == Guid.Empty</c>): writes a
///   <b>shared blueprint</b> row (<c>TenantId = null</c>) under a suppressed guard —
///   the dev/admin vocabulary-edit affordance (per-row override spec's default-tenant
///   mode). In production the API pipeline guarantees a real <c>tenant_id</c> claim
///   (FR-19), so <c>Guid.Empty</c> only occurs in dev/test.</item>
/// </list>
/// <para><b>Duplicate-code guard</b> (§3.4 / FR-6): before creating, rejects if a
/// coded value with the same <c>(parent, code)</c> already exists in the
/// tenant-visible scope (shared blueprint ∪ this tenant's owned rows for the real
/// path; shared blueprint only for the default path), throwing
/// <see cref="CodedValueCodeConflictException"/> that directs the caller to
/// <b>override the shared row's name</b> (via <c>UpsertCodedValueOverride</c>)
/// instead of creating a duplicate.</para>
/// </summary>
public sealed class CreateCodedValueHandler(
    ICodedValueRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ITenantContextAccessor tenantContextAccessor,
    ILogger<CreateCodedValueHandler> logger) : ICommandHandler<CreateCodedValue, Guid>
{
    public async Task<Guid> HandleAsync(CreateCodedValue command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CreateCodedValue {Code}", command.Code);

        var code = command.Code.Trim().ToUpperInvariant();
        var currentTenantId = tenantProvider.GetTenantContext().TenantId;
        var isDefaultTenant = currentTenantId == Guid.Empty;

        // FR-5: real tenant → tenant-owned row; default/dev → NULL shared blueprint.
        var targetTenantId = isDefaultTenant ? (Guid?)null : currentTenantId;

        // FR-6 duplicate-code guard. FindConflictingByCodeAndParentAsync ignores the
        // "Tenant" filter and matches (parent, code) across shared (NULL) ∪ the
        // relevant owned scope. Passing targetTenantId=null (default path) limits the
        // owned match to NULL rows only.
        var conflicting = await repository.FindConflictingByCodeAndParentAsync(
            code, command.ParentId, targetTenantId, cancellationToken);
        if (conflicting is not null)
        {
            throw new CodedValueCodeConflictException(
                code,
                command.ParentId,
                conflicting.Id,
                existingIsSharedBlueprint: conflicting.TenantId == null);
        }

        // FR-6: DisplayOrder uniqueness for GRADE children. DisplayOrder IS the grade
        // level (no separate Level column), so siblings must not share the same value.
        if (command.ParentId is not null)
        {
            var parent = await repository.GetAsync(command.ParentId.Value, cancellationToken);
            if (parent is not null && parent.Code == "GRADE")
            {
                var duplicateLevel = await repository.FindSiblingByDisplayOrderAsync(
                    command.ParentId.Value, command.DisplayOrder, cancellationToken);
                if (duplicateLevel is not null)
                {
                    throw new DuplicateGradeLevelException(command.DisplayOrder, duplicateLevel.Id);
                }
            }
        }

        var codedValue = CodedValue.Create(
            command.Code,
            command.Name,
            command.Description,
            command.ParentId,
            command.DisplayOrder);
        codedValue.SetTenant(targetTenantId);

        // FR-5: the default/dev path writes a NULL-blueprint row under a suppressed
        // guard (belt-and-suspenders — the hybrid save-guard already permits NULL;
        // this documents intent and is robust to future guard changes).
        if (isDefaultTenant)
        {
            using (tenantContextAccessor.SuppressTenantGuard())
            {
                await repository.AddAsync(codedValue, cancellationToken);
            }
        }
        else
        {
            await repository.AddAsync(codedValue, cancellationToken);
        }

        await cache.RemoveByTagAsync("coded-values", cancellationToken);

        foreach (var _ in codedValue.DomainEvents.OfType<CodedValueCreatedEvent>())
        {
            await publisher.EnqueueAsync(new CodedValueCreated(
                codedValue.Id,
                codedValue.Code,
                codedValue.Name,
                codedValue.Description,
                codedValue.ParentId,
                codedValue.DisplayOrder,
                codedValue.CreatedAt), cancellationToken);
        }

        codedValue.ClearDomainEvents();

        logger.LogInformation(
            "CodedValue {Id} persisted with code {Code} (tenant={TenantKind})",
            codedValue.Id, codedValue.Code, isDefaultTenant ? "shared-blueprint" : "tenant-owned");
        return codedValue.Id;
    }
}
