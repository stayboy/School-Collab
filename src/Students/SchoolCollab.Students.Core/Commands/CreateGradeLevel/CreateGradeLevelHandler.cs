using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Commands.CreateGradeLevel;

public sealed class CreateGradeLevelHandler(
    IGradeLevelRepository repository,
    HybridCache cache,
    ILogger<CreateGradeLevelHandler> logger) : ICommandHandler<CreateGradeLevel, Guid>
{
    public async Task<Guid> HandleAsync(CreateGradeLevel command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CreateGradeLevel {Name}", command.Name);

        var gradeLevel = GradeLevel.Create(
            command.CodedValueId,
            command.Level,
            command.Name,
            command.DisplayOrder);

        await repository.AddAsync(gradeLevel, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        gradeLevel.ClearDomainEvents();

        logger.LogInformation("GradeLevel {Id} created with name {Name}", gradeLevel.Id, gradeLevel.Name);
        return gradeLevel.Id;
    }
}