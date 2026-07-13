using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherSubject;

public sealed class UnlinkTeacherSubjectHandler(
    ITeacherRepository repository,
    HybridCache cache,
    ILogger<UnlinkTeacherSubjectHandler> logger) : ICommandHandler<UnlinkTeacherSubject>
{
    public async Task HandleAsync(UnlinkTeacherSubject command, CancellationToken cancellationToken = default)
    {
        await repository.RemoveSubjectAsync(command.TeacherId, command.SubjectId, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Subject {SubjectId} unlinked from teacher {TeacherId}", command.SubjectId, command.TeacherId);
    }
}
