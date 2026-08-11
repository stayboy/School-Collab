using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacherActivityAssignment;

public sealed class DeleteTeacherActivityAssignmentHandler(
    ITeacherRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<DeleteTeacherActivityAssignmentHandler> logger) : ICommandHandler<DeleteTeacherActivityAssignment>
{
    public async Task HandleAsync(DeleteTeacherActivityAssignment command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(DeleteTeacherActivityAssignment), typeof(TeacherActivityAssignment));

        var link = await repository.GetActivityAssignmentByIdAsync(command.RowId, cancellationToken)
            ?? throw new TeacherLinkNotFoundException(command.TeacherId, command.RowId);
        if (link.TeacherId != command.TeacherId)
            throw new TeacherLinkNotFoundException(command.TeacherId, command.RowId);

        await repository.RemoveActivityAssignmentAsync(command.RowId, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Activity assignment row {RowId} removed for teacher {TeacherId}", command.RowId, command.TeacherId);
    }
}
