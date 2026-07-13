using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherSubject;

public sealed class LinkTeacherSubjectHandler(
    ITeacherRepository repository,
    ISubjectRepository subjectRepository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<LinkTeacherSubjectHandler> logger) : ICommandHandler<LinkTeacherSubject>
{
    public async Task HandleAsync(LinkTeacherSubject command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(LinkTeacherSubject), typeof(TeacherSubject));

        // Validate both sides exist (tenant-scoped; soft-deleted teachers are
        // excluded so a blocked record cannot receive new links). Mirrors
        // LinkGuardianToStudentHandler (spec §4.12).
        if (await repository.GetAsync(command.TeacherId, cancellationToken) is null)
            throw new TeacherNotFoundException(command.TeacherId);

        if (await subjectRepository.GetAsync(command.SubjectId, cancellationToken) is null)
            throw new SubjectNotFoundException(command.SubjectId);

        if (await repository.GetSubjectLinkAsync(command.TeacherId, command.SubjectId, cancellationToken) is not null)
            throw new TeacherLinkAlreadyExistsException(command.TeacherId, command.SubjectId);

        var link = TeacherSubject.Create(command.TeacherId, command.SubjectId)
            .WithTenant(tenantProvider);
        await repository.AddSubjectAsync(link, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Subject {SubjectId} linked to teacher {TeacherId}", command.SubjectId, command.TeacherId);
    }
}