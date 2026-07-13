using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherSubject;

public sealed class LinkTeacherSubjectHandler(
    ITeacherRepository repository,
    ITenantProvider tenantProvider,
    HybridCache cache,
    ILogger<LinkTeacherSubjectHandler> logger) : ICommandHandler<LinkTeacherSubject>
{
    public async Task HandleAsync(LinkTeacherSubject command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(LinkTeacherSubject), typeof(TeacherSubject));

        var link = TeacherSubject.Create(command.TeacherId, command.SubjectId)
            .WithTenant(tenantProvider);
        await repository.AddSubjectAsync(link, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Subject {SubjectId} linked to teacher {TeacherId}", command.SubjectId, command.TeacherId);
    }
}
