using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubject;

public sealed class CreateSubjectHandler(
    ISubjectRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateSubjectHandler> logger) : ICommandHandler<CreateSubject, Guid>
{
    public async Task<Guid> HandleAsync(CreateSubject command, CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreateSubject), typeof(Subject));

        logger.LogDebug("Handling CreateSubject {Code}", command.Code);

        if (await repository.ExistsByCodeAsync(command.Code, cancellationToken))
            throw new DuplicateSubjectCodeException(command.Code);

        var subject = Subject.Create(
            command.CodedValueId,
            command.Code,
            command.Name,
            command.DisplayOrder)
            .WithTenant(tenantProvider);

        await repository.AddAsync(subject, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        subject.ClearDomainEvents();

        logger.LogInformation("Subject {Id} created with code {Code}", subject.Id, subject.Code);
        return subject.Id;
    }
}