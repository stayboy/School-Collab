using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopic;

public sealed class CreateTopicHandler(
    ITopicRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateTopicHandler> logger) : ICommandHandler<CreateTopic, Guid>
{
    public async Task<Guid> HandleAsync(CreateTopic command, CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreateTopic), typeof(Topic));

        logger.LogDebug("Handling CreateTopic {Code}", command.Code);

        if (await repository.ExistsByCodeAsync(command.Code, cancellationToken))
            throw new DuplicateTopicCodeException(command.Code);

        var subject = Topic.Create(
            command.CodedValueId,
            command.Code,
            command.Name,
            command.DisplayOrder)
            .WithTenant(tenantProvider);

        await repository.AddAsync(subject, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        subject.ClearDomainEvents();

        logger.LogInformation("Topic {Id} created with code {Code}", subject.Id, subject.Code);
        return subject.Id;
    }
}