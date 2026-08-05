using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherTopic;

public sealed class LinkTeacherTopicHandler(
    ITeacherRepository repository,
    ITopicRepository subjectRepository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<LinkTeacherTopicHandler> logger) : ICommandHandler<LinkTeacherTopic>
{
    public async Task HandleAsync(LinkTeacherTopic command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(LinkTeacherTopic), typeof(TeacherTopic));

        // Validate both sides exist (tenant-scoped; soft-deleted teachers are
        // excluded so a blocked record cannot receive new links). Mirrors
        // LinkGuardianToStudentHandler (spec §4.12).
        if (await repository.GetAsync(command.TeacherId, cancellationToken) is null)
            throw new TeacherNotFoundException(command.TeacherId);

        if (await subjectRepository.GetAsync(command.TopicId, cancellationToken) is null)
            throw new TopicNotFoundException(command.TopicId);

        if (await repository.GetTopicLinkAsync(command.TeacherId, command.TopicId, cancellationToken) is not null)
            throw new TeacherLinkAlreadyExistsException(command.TeacherId, command.TopicId);

        var link = TeacherTopic.Create(command.TeacherId, command.TopicId)
            .WithTenant(tenantProvider);
        await repository.AddTopicAsync(link, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Topic {TopicId} linked to teacher {TeacherId}", command.TopicId, command.TeacherId);
    }
}