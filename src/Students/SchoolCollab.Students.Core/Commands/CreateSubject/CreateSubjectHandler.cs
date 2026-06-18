using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.Commands.CreateSubject;

public sealed class CreateSubjectHandler(
    ISubjectRepository repository,
    HybridCache cache,
    ILogger<CreateSubjectHandler> logger) : ICommandHandler<CreateSubject, Guid>
{
    public async Task<Guid> HandleAsync(CreateSubject command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling CreateSubject {Code}", command.Code);

        if (await repository.ExistsByCodeAsync(command.Code, cancellationToken))
            throw new DuplicateSubjectCodeException(command.Code);

        var subject = Subject.Create(
            command.CodedValueId,
            command.Code,
            command.Name,
            command.DisplayOrder);

        await repository.AddAsync(subject, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        subject.ClearDomainEvents();

        logger.LogInformation("Subject {Id} created with code {Code}", subject.Id, subject.Code);
        return subject.Id;
    }
}