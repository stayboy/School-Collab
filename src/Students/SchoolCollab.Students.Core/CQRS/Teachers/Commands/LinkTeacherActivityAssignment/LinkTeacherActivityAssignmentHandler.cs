using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherActivityAssignment;

public sealed class LinkTeacherActivityAssignmentHandler(
    ITeacherRepository repository,
    StudentsDbContext db,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<LinkTeacherActivityAssignmentHandler> logger) : ICommandHandler<LinkTeacherActivityAssignment>
{
    public async Task HandleAsync(LinkTeacherActivityAssignment command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(LinkTeacherActivityAssignment), typeof(TeacherActivityAssignment));

        if (await repository.GetAsync(command.TeacherId, cancellationToken) is null)
            throw new TeacherNotFoundException(command.TeacherId);

        if (await db.ActivityGroups.AnyAsync(a => a.Id == command.ActivityGroupId, cancellationToken) is false)
            throw new ActivityGroupNotFoundException(command.ActivityGroupId);

        var link = TeacherActivityAssignment.Create(
                command.TeacherId, command.ActivityGroupId, command.RoleCodedValueId, command.GradeLevelIds)
            .WithTenant(tenantProvider);
        await repository.AddActivityAssignmentAsync(link, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Activity {ActivityGroupId} linked to teacher {TeacherId}", command.ActivityGroupId, command.TeacherId);
    }
}
