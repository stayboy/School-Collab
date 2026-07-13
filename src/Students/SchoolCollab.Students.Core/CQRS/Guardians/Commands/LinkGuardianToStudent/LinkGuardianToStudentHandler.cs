using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.LinkGuardianToStudent;

public sealed class LinkGuardianToStudentHandler(
    IStudentRepository studentRepository,
    IGuardianRepository guardianRepository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<LinkGuardianToStudentHandler> logger) : ICommandHandler<LinkGuardianToStudent, Guid>
{
    public async Task<Guid> HandleAsync(LinkGuardianToStudent command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(LinkGuardianToStudent), typeof(StudentGuardian));

        if (await studentRepository.GetAsync(command.StudentId, cancellationToken) is null)
            throw new StudentNotFoundException(command.StudentId);

        if (await guardianRepository.GetAsync(command.GuardianId, cancellationToken) is null)
            throw new GuardianNotFoundException(command.GuardianId);

        if (await guardianRepository.GetLinkAsync(command.StudentId, command.GuardianId, cancellationToken) is not null)
            throw new GuardianLinkAlreadyExistsException(command.StudentId, command.GuardianId);

        var link = StudentGuardian.Create(
                command.StudentId,
                command.GuardianId,
                command.Role,
                command.RelationshipCodedValueId,
                command.IsEmergencyContact,
                command.ActingGuardianId)
            .WithTenant(tenantProvider);

        await guardianRepository.AddLinkAsync(link, cancellationToken);
        await cache.RemoveByTagAsync("guardians", cancellationToken);

        logger.LogInformation(
            "Linked student {StudentId} to guardian {GuardianId} as {Role} (acting: {Acting})",
            command.StudentId, command.GuardianId, command.Role, command.ActingGuardianId);
        return link.Id;
    }
}
