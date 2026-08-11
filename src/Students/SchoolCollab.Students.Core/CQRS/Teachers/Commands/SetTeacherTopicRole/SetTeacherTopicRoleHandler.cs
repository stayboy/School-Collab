using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.SetTeacherTopicRole;

public sealed class SetTeacherTopicRoleHandler(
    ITeacherRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<SetTeacherTopicRoleHandler> logger) : ICommandHandler<SetTeacherTopicRole>
{
    public async Task HandleAsync(SetTeacherTopicRole command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(SetTeacherTopicRole), typeof(TeacherTopic));

        var link = await repository.GetTopicLinkAsync(command.TeacherId, command.TopicId, cancellationToken)
            ?? throw new TeacherLinkNotFoundException(command.TeacherId, command.TopicId);

        // Idempotent at the domain layer — SetRole no-ops when unchanged.
        link.SetRole(command.RoleCodedValueId, command.StartDate, command.EndDate);
        await repository.UpdateTopicAsync(link, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation(
            "Set teacher {TeacherId} role on topic {TopicId} to {Role}",
            command.TeacherId, command.TopicId,
            command.RoleCodedValueId is null ? "(none)" : command.RoleCodedValueId.Value.ToString());
    }
}
