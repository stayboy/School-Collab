using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherGradeLevel;

public sealed class LinkTeacherGradeLevelHandler(
    ITeacherRepository repository,
    IGradeLevelRepository gradeLevelRepository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<LinkTeacherGradeLevelHandler> logger) : ICommandHandler<LinkTeacherGradeLevel>
{
    public async Task HandleAsync(LinkTeacherGradeLevel command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(LinkTeacherGradeLevel), typeof(TeacherGradeLevel));

        // Validate both sides exist (tenant-scoped; soft-deleted teachers are
        // excluded so a blocked record cannot receive new links). Mirrors
        // LinkGuardianToStudentHandler (spec §4.12).
        if (await repository.GetAsync(command.TeacherId, cancellationToken) is null)
            throw new TeacherNotFoundException(command.TeacherId);

        if (await gradeLevelRepository.GetAsync(command.GradeLevelId, cancellationToken) is null)
            throw new GradeLevelNotFoundException(command.GradeLevelId);

        if (await repository.GetGradeLevelLinkAsync(command.TeacherId, command.GradeLevelId, cancellationToken) is not null)
            throw new TeacherLinkAlreadyExistsException(command.TeacherId, command.GradeLevelId);

        var link = TeacherGradeLevel.Create(command.TeacherId, command.GradeLevelId, command.TeacherRoleCodedValueId)
            .WithTenant(tenantProvider);
        await repository.AddGradeLevelAsync(link, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Grade level {GradeLevelId} linked to teacher {TeacherId}", command.GradeLevelId, command.TeacherId);
    }
}