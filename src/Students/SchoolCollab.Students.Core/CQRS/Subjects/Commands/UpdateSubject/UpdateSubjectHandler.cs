using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.UpdateSubject;

public sealed class UpdateSubjectHandler(
    ISubjectRepository repository,
    HybridCache cache,
    ILogger<UpdateSubjectHandler> logger) : ICommandHandler<UpdateSubject>
{
    public async Task HandleAsync(UpdateSubject command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdateSubject {Id}", command.Id);

        var subject = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new SubjectNotFoundException(command.Id);

        subject.Update(command.Name, command.DisplayOrder);

        try
        {
            await repository.UpdateAsync(subject, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Subject", subject.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        subject.ClearDomainEvents();

        logger.LogInformation("Subject {Id} updated", subject.Id);
    }
}