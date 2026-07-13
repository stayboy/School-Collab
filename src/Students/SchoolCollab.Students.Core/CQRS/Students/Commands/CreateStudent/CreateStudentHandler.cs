using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.CQRS.Students.Commands.CreateStudent;

public sealed class CreateStudentHandler(
    IStudentRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateStudentHandler> logger) : ICommandHandler<CreateStudent, Guid>
{
    public async Task<Guid> HandleAsync(CreateStudent command, CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreateStudent), typeof(Student));

        logger.LogDebug("Handling CreateStudent {StudentNumber}", command.StudentNumber);

        if (await repository.ExistsByStudentNumberAsync(command.StudentNumber, cancellationToken))
            throw new DuplicateStudentNumberException(command.StudentNumber);

        var tenantContext = tenantProvider.GetTenantContext();

        var student = Student.Create(
            command.StudentNumber,
            command.FirstName,
            command.LastName,
            command.DateOfBirth,
            command.GenderCodedValueId)
            .WithTenant(tenantProvider);

        await repository.AddAsync(student, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);

        foreach (var _ in student.DomainEvents.OfType<StudentCreatedEvent>())
        {
            await publisher.EnqueueAsync(new StudentCreated(
                student.Id,
                student.StudentNumber,
                student.FirstName,
                student.LastName,
                student.CreatedAt), cancellationToken);
        }

        student.ClearDomainEvents();

        logger.LogInformation("Student {Id} created with number {StudentNumber} for tenant {TenantId}", student.Id, student.StudentNumber, tenantContext.TenantId);
        return student.Id;
    }
}