using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherGradeLevel;

public sealed class LinkTeacherGradeLevelHandler(
    ITeacherRepository repository,
    ITenantProvider tenantProvider,
    HybridCache cache,
    ILogger<LinkTeacherGradeLevelHandler> logger) : ICommandHandler<LinkTeacherGradeLevel>
{
    public async Task HandleAsync(LinkTeacherGradeLevel command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(LinkTeacherGradeLevel), typeof(TeacherGradeLevel));

        var link = TeacherGradeLevel.Create(command.TeacherId, command.GradeLevelId)
            .WithTenant(tenantProvider);
        await repository.AddGradeLevelAsync(link, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Grade level {GradeLevelId} linked to teacher {TeacherId}", command.GradeLevelId, command.TeacherId);
    }
}
