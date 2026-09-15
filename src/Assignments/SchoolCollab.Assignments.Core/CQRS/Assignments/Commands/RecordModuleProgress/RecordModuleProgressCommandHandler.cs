using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RecordModuleProgress;

/// <summary>
/// WS-D1 upsert handler (spec §3.3, decision (d)). Loads the assignment to
/// resolve the module's completion threshold (the assignment aggregate owns
/// the gating defaults), then upserts the ward's progress row — first report
/// creates it via the domain factory, later reports <see cref="ModuleProgress.Record"/>
/// monotonically. One <c>SaveChangesAsync</c>; the module threshold is applied
/// so <see cref="ModuleProgress.CompletedAt"/> stamps on first meet.
/// </summary>
public sealed class RecordModuleProgressCommandHandler(
    IAssignmentRepository assignmentRepository,
    IModuleProgressRepository moduleProgressRepository,
    ITenantProvider tenantProvider,
    ILogger<RecordModuleProgressCommandHandler> logger) : ICommandHandler<RecordModuleProgressCommand, bool>
{
    public async Task<bool> HandleAsync(RecordModuleProgressCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling RecordModuleProgress for assignment {AssignmentId} / student {StudentId} / module {ContentModuleId}",
            command.AssignmentId, command.StudentId, command.ContentModuleId);

        var assignment = await assignmentRepository.GetAsync(command.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(command.AssignmentId);

        var module = assignment.Modules.SingleOrDefault(m => m.Id == command.ContentModuleId);
        if (module is null)
        {
            // Module not on the assignment → the route maps to 404.
            return false;
        }

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var progress = await moduleProgressRepository.GetAsync(
            command.AssignmentId, command.StudentId, command.ContentModuleId, cancellationToken);

        if (progress is null)
        {
            progress = ModuleProgress.Create(
                tenantId, command.AssignmentId, command.StudentId,
                command.ContentModuleId, command.Percent, module.MinCompletionThresholdPercent);
            moduleProgressRepository.Add(progress);
        }
        else
        {
            progress.Record(command.Percent, module.MinCompletionThresholdPercent);
        }

        await moduleProgressRepository.SaveChangesAsync(cancellationToken);
        return true;
    }
}
