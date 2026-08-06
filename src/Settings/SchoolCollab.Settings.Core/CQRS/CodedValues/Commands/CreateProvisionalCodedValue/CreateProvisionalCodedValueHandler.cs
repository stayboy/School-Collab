using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Events;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateProvisionalCodedValue;

/// <summary>
/// Creates a <b>tenant-owned, provisional</b> coded value (tcv/3, spec §C).
/// Unlike <see cref="CreateCodedValue"/>, this path <b>always</b> stamps a real
/// tenant (<c>TenantId = current</c>) and marks the row <c>IsProvisional = true</c>
/// so it is hidden from other tenants until a system-wide approval promotes it to
/// the shared blueprint. Rejects (default/dev tenant, no tenant in scope) because a
/// provisional value must belong to a real tenant awaiting approval.
///
/// <para>The duplicate-code guard matches the shared-blueprint scope ∪ the current
/// tenant's owned rows (FR-6), so a tenant cannot squat on a code that already
/// exists globally.</para>
/// </summary>
public sealed class CreateProvisionalCodedValueHandler(
    ICodedValueRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateProvisionalCodedValueHandler> logger) : ICommandHandler<CreateProvisionalCodedValue, Guid>
{
    public async Task<Guid> HandleAsync(CreateProvisionalCodedValue command, CancellationToken cancellationToken = default)
    {
        var code = command.Code.Trim().ToUpperInvariant();
        var currentTenantId = tenantProvider.GetTenantContext().TenantId;
        if (currentTenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "A provisional coded value requires a real tenant in scope (default/dev tenant cannot create one).");
        }

        // FR-6 duplicate-code guard within the tenant-visible scope (shared blueprint
        // ∪ this tenant's owned rows).
        var conflicting = await repository.FindConflictingByCodeAndParentAsync(
            code, command.ParentId, currentTenantId, cancellationToken);
        if (conflicting is not null)
        {
            throw new CodedValueCodeConflictException(
                code,
                command.ParentId,
                conflicting.Id,
                existingIsSharedBlueprint: conflicting.TenantId == null);
        }

        var codedValue = CodedValue.Create(
            command.Code,
            command.Name,
            command.Description,
            command.ParentId,
            command.DisplayOrder);
        codedValue.SetTenant(currentTenantId);
        codedValue.MarkProvisional();

        await repository.AddAsync(codedValue, cancellationToken);
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
            "Provisional CodedValue {Id} created for tenant {TenantId} with code {Code}",
            codedValue.Id, currentTenantId, codedValue.Code);
        return codedValue.Id;
    }
}
