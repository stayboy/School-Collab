using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherGradeLevel;

public sealed class UnlinkTeacherGradeLevelHandler(
    ITeacherRepository repository,
    HybridCache cache,
    ILogger<UnlinkTeacherGradeLevelHandler> logger) : ICommandHandler<UnlinkTeacherGradeLevel>
{
    public async Task HandleAsync(UnlinkTeacherGradeLevel command, CancellationToken cancellationToken = default)
    {
        await repository.RemoveGradeLevelAsync(command.TeacherId, command.GradeLevelId, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Grade level {GradeLevelId} unlinked from teacher {TeacherId}", command.GradeLevelId, command.TeacherId);
    }
}
