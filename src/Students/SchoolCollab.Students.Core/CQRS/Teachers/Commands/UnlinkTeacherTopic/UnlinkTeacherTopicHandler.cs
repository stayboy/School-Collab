using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherTopic;

public sealed class UnlinkTeacherTopicHandler(
    ITeacherRepository repository,
    HybridCache cache,
    ILogger<UnlinkTeacherTopicHandler> logger) : ICommandHandler<UnlinkTeacherTopic>
{
    public async Task HandleAsync(UnlinkTeacherTopic command, CancellationToken cancellationToken = default)
    {
        await repository.RemoveTopicAsync(command.TeacherId, command.TopicId, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Topic {TopicId} unlinked from teacher {TeacherId}", command.TopicId, command.TeacherId);
    }
}
