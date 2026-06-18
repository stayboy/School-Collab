using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Messaging;

namespace SchoolCollab.Students.Core.Commands.DeleteStudent;

public sealed class DeleteStudentHandler(
    IStudentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<DeleteStudentHandler> logger) : ICommandHandler<DeleteStudent>
{
    public async Task HandleAsync(DeleteStudent command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DeleteStudent {Id}", command.Id);

        var student = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new StudentNotFoundException(command.Id);

        student.Delete();

        try
        {
            await repository.UpdateAsync(student, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Student", student.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        foreach (var _ in student.DomainEvents.OfType<StudentDeletedEvent>())
        {
            await publisher.EnqueueAsync(new StudentDeleted(
                student.Id,
                student.StudentNumber,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        student.ClearDomainEvents();

        logger.LogInformation("Student {Id} deleted", student.Id);
    }
}