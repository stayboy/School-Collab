using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.UpdateGradeLevel;

public sealed class UpdateGradeLevelHandler(
    IGradeLevelRepository repository,
    HybridCache cache,
    ILogger<UpdateGradeLevelHandler> logger) : ICommandHandler<UpdateGradeLevel>
{
    public async Task HandleAsync(UpdateGradeLevel command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdateGradeLevel {Id}", command.Id);

        var gradeLevel = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new GradeLevelNotFoundException(command.Id);

        gradeLevel.Update(command.Level, command.Name, command.DisplayOrder,
            command.MinAge, command.MaxAge, command.AllowedGenderCodedValueId);

        try
        {
            await repository.UpdateAsync(gradeLevel, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("GradeLevel", gradeLevel.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        gradeLevel.ClearDomainEvents();

        logger.LogInformation("GradeLevel {Id} updated", gradeLevel.Id);
    }
}