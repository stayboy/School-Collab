using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.CreateGradeLevel;

public sealed class CreateGradeLevelHandler(
    IGradeLevelRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateGradeLevelHandler> logger) : ICommandHandler<CreateGradeLevel, Guid>
{
    public async Task<Guid> HandleAsync(CreateGradeLevel command, CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreateGradeLevel), typeof(GradeLevel));

        logger.LogDebug("Handling CreateGradeLevel {Name}", command.Name);

        var gradeLevel = GradeLevel.Create(
            command.CodedValueId,
            command.Level,
            command.Name,
            command.DisplayOrder,
            command.MinAge,
            command.MaxAge,
            command.AllowedGenderCodedValueId)
            .WithTenant(tenantProvider);

        await repository.AddAsync(gradeLevel, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        gradeLevel.ClearDomainEvents();

        logger.LogInformation("GradeLevel {Id} created with name {Name}", gradeLevel.Id, gradeLevel.Name);
        return gradeLevel.Id;
    }
}