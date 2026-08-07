using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.CreateGuardian;

public sealed class CreateGuardianHandler(
    IGuardianRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateGuardianHandler> logger) : ICommandHandler<CreateGuardian, Guid>
{
    public async Task<Guid> HandleAsync(CreateGuardian command, CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreateGuardian), typeof(Guardian));

        logger.LogDebug("Handling CreateGuardian {LastName}", command.LastName);

        var guardian = Guardian.Create(
                command.TitleCodedValueId,
                command.FirstName,
                command.LastName,
                command.DisplayName,
                command.Address,
                command.CommunityId,
                command.DateOfBirth,
                command.GenderCodedValueId)
            .WithTenant(tenantProvider);

        guardian.AddInitialNameHistory();

        await repository.AddAsync(guardian, cancellationToken);
        await repository.PersistNameHistoryAsync(guardian, cancellationToken);
        await cache.RemoveByTagAsync("guardians", cancellationToken);

        logger.LogInformation("Guardian {Id} created for tenant {TenantId}", guardian.Id, guardian.TenantId);
        return guardian.Id;
    }
}
