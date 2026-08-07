using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.SetTeacherGradeLevelRole;

public sealed class SetTeacherGradeLevelRoleHandler(
    ITeacherRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<SetTeacherGradeLevelRoleHandler> logger) : ICommandHandler<SetTeacherGradeLevelRole>
{
    public async Task HandleAsync(SetTeacherGradeLevelRole command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(SetTeacherGradeLevelRole), typeof(TeacherGradeLevel));

        var link = await repository.GetGradeLevelLinkAsync(command.TeacherId, command.GradeLevelId, cancellationToken)
            ?? throw new TeacherLinkNotFoundException(command.TeacherId, command.GradeLevelId);

        // Idempotent at the domain layer — SetRole no-ops when unchanged.
        link.SetRole(command.TeacherRoleCodedValueId);
        await repository.UpdateGradeLevelAsync(link, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation(
            "Set teacher {TeacherId} role on grade {GradeLevelId} to {Role}",
            command.TeacherId, command.GradeLevelId,
            command.TeacherRoleCodedValueId is null ? "(none)" : command.TeacherRoleCodedValueId.Value.ToString());
    }
}
