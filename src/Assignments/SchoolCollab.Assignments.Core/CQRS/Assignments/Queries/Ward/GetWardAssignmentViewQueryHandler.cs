using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.Ward;

/// <summary>
/// WS-D1/WS-A5 (spec §3.3) — one ward's view of a single assignment: the
/// content modules with the ward's progress merged in, and the
/// questions-unlocked flag (all required modules completed). The 2b ward
/// player binds to <see cref="WardModuleViewDto.PercentComplete"/> /
/// <see cref="WardModuleViewDto.CompletedAt"/> and gates the question block
/// on <see cref="WardAssignmentViewDto.QuestionsUnlocked"/>.
/// </summary>
public sealed class GetWardAssignmentViewHandler(
    IAssignmentRepository assignmentRepository,
    IModuleProgressRepository moduleProgressRepository,
    ILogger<GetWardAssignmentViewHandler> logger) : IQueryHandler<GetWardAssignmentView, WardAssignmentViewDto?>
{
    public async Task<WardAssignmentViewDto?> HandleAsync(GetWardAssignmentView query, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling GetWardAssignmentView for assignment {AssignmentId} / student {StudentId}",
            query.AssignmentId, query.StudentId);

        var assignment = await assignmentRepository.GetAsync(query.AssignmentId, cancellationToken);
        if (assignment is null)
        {
            return null;
        }

        var progressByModule = (await moduleProgressRepository.ListProgressForAssignmentStudentAsync(
            query.AssignmentId, query.StudentId, cancellationToken))
            .ToDictionary(p => p.ContentModuleId);

        var modules = assignment.Modules
            .OrderBy(m => m.DisplayOrder)
            .Select(m =>
            {
                progressByModule.TryGetValue(m.Id, out var progress);
                return new WardModuleViewDto(
                    m.Id,
                    ToDto(m.ModuleType),
                    m.Title,
                    m.Url,
                    m.DisplayOrder,
                    m.MinCompletionThresholdPercent,
                    m.IsRequired,
                    progress?.PercentComplete ?? 0,
                    progress?.CompletedAt);
            })
            .ToList();

        var questionsUnlocked = assignment.Modules
            .Where(m => m.IsRequired)
            .All(m => progressByModule.TryGetValue(m.Id, out var p) && p.CompletedAt is not null);

        return new WardAssignmentViewDto(
            query.AssignmentId, assignment.Title, assignment.DueDate, questionsUnlocked, modules);
    }

    /// <summary>Domain <see cref="ModuleType"/> → contract <see cref="ModuleTypeDto"/>
    /// (both enums are value-aligned Video=0/Guide=1; maps explicitly so a future
    /// renumber on either side cannot silently misalign the wire contract).</summary>
    private static ModuleTypeDto ToDto(ModuleType moduleType) => moduleType switch
    {
        ModuleType.Video => ModuleTypeDto.Video,
        _ => ModuleTypeDto.Guide
    };
}
