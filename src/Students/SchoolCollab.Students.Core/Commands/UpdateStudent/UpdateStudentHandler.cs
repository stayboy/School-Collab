using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Messaging;

namespace SchoolCollab.Students.Core.Commands.UpdateStudent;

public sealed class UpdateStudentHandler(
    IStudentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<UpdateStudentHandler> logger) : ICommandHandler<UpdateStudent>
{
    public async Task HandleAsync(UpdateStudent command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdateStudent {Id}", command.Id);

        var student = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new StudentNotFoundException(command.Id);

        student.Update(
            command.FirstName,
            command.LastName,
            command.DateOfBirth,
            command.GenderCodedValueId,
            command.ContactEmail,
            command.ContactPhone);

        try
        {
            await repository.UpdateAsync(student, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Student", student.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        foreach (var _ in student.DomainEvents.OfType<StudentUpdatedEvent>())
        {
            await publisher.EnqueueAsync(new StudentUpdated(
                student.Id,
                student.StudentNumber,
                student.FirstName,
                student.LastName,
                student.UpdatedAt), cancellationToken);
        }

        student.ClearDomainEvents();

        logger.LogInformation("Student {Id} updated", student.Id);
    }
}