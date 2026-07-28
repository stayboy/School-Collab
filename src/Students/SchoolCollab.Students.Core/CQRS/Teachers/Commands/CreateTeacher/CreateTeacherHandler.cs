using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.CreateTeacher;

public sealed class CreateTeacherHandler(
    ITeacherRepository repository,
    IEntityCodeGenerator entityCodeGenerator,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateTeacherHandler> logger) : ICommandHandler<CreateTeacher, Guid>
{
    public async Task<Guid> HandleAsync(CreateTeacher command, CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreateTeacher), typeof(Teacher));

        // Spec §4.5: auto-generate the staff number before constructing the entity.
        var staffNumber = await entityCodeGenerator.GenerateAsync("STAFF_CODE", cancellationToken);

        var teacher = Teacher.Create(
                command.TitleCodedValueId,
                command.FirstName,
                command.LastName,
                command.DisplayName,
                command.Email,
                command.ContactPhone,
                staffNumber: staffNumber)
            .WithTenant(tenantProvider);

        await repository.AddAsync(teacher, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Teacher {Id} created with staff number {StaffNumber} for tenant {TenantId}",
            teacher.Id, teacher.StaffNumber, teacher.TenantId);
        return teacher.Id;
    }
}
