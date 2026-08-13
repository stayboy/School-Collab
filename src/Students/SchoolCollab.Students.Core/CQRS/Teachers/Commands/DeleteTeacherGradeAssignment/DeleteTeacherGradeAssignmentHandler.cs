using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacherGradeAssignment;

public sealed class DeleteTeacherGradeAssignmentHandler(
    ITeacherRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<DeleteTeacherGradeAssignmentHandler> logger) : ICommandHandler<DeleteTeacherGradeAssignment>
{
    public async Task HandleAsync(DeleteTeacherGradeAssignment command, CancellationToken cancellationToken = default)
    {
        tenantProvider.RequireTenantContext(nameof(DeleteTeacherGradeAssignment), typeof(TeacherGradeLevel));

        var link = await repository.GetGradeLevelLinkByIdAsync(command.RowId, cancellationToken)
            ?? throw new TeacherLinkNotFoundException(command.TeacherId, command.RowId);
        if (link.TeacherId != command.TeacherId)
            throw new TeacherLinkNotFoundException(command.TeacherId, command.RowId);

        await repository.RemoveGradeLevelRowAsync(command.RowId, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Grade assignment row {RowId} removed for teacher {TeacherId}", command.RowId, command.TeacherId);
    }
}
