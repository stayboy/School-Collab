using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.CQRS.Students.Commands.CreateStudent;

public sealed class CreateStudentHandler(
    IStudentRepository repository,
    IEntityCodeGenerator entityCodeGenerator,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateStudentHandler> logger) : ICommandHandler<CreateStudent, Guid>
{
    public async Task<Guid> HandleAsync(CreateStudent command, CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreateStudent), typeof(Student));

        // Spec §4.4: auto-generate the student number before constructing the entity.
        // The generated code is the canonical StudentNumber; command.StudentNumber is
        // retained for API compatibility but is not used.
        var studentNumber = await entityCodeGenerator.GenerateAsync("STUDENT_CODE", cancellationToken);

        if (await repository.ExistsByStudentNumberAsync(studentNumber, cancellationToken))
            throw new DuplicateStudentNumberException(studentNumber);

        var tenantContext = tenantProvider.GetTenantContext();

        var student = Student.Create(
            studentNumber,
            command.FirstName,
            command.LastName,
            command.DateOfBirth,
            command.GenderCodedValueId,
            command.TitleCodedValueId)
            .WithTenant(tenantProvider);

        foreach (var _ in student.DomainEvents.OfType<StudentCreatedEvent>())
        {
            await publisher.EnqueueAsync(new StudentCreated(
                student.Id,
                student.StudentNumber,
                student.FirstName,
                student.LastName,
                student.CreatedAt), cancellationToken);
        }

        await repository.AddAsync(student, cancellationToken);
        await cache.RemoveByTagAsync("students", cancellationToken);


        student.ClearDomainEvents();

        logger.LogInformation("Student {Id} created with number {StudentNumber} for tenant {TenantId}",
            student.Id, student.StudentNumber, tenantContext.TenantId);
        return student.Id;
    }
}