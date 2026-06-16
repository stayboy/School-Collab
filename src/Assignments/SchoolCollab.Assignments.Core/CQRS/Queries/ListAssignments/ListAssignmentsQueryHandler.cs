using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.DTOs;

namespace SchoolCollab.Assignments.Core.Queries.ListAssignments;

public sealed class ListAssignmentsQueryHandler(
    IAssignmentRepository repository,
    ILogger<ListAssignmentsQueryHandler> logger) : IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]>
{
    public async Task<AssignmentSummaryDto[]> HandleAsync(
        ListAssignmentsQuery query,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ListAssignmentsQuery with status {Status}", query.Status?.ToString() ?? "all");

        var summaries = await repository.ListAsync(query.Status, cancellationToken);

        var dtos = summaries.Select(s => new AssignmentSummaryDto(
            s.Id,
            s.Title,
            s.Description ?? string.Empty,
            s.AssignmentType.ToString(),
            s.GradingFormat.ToString(),
            s.TargetAudience.ToString(),
            s.SubjectCodedValueId,
            s.GradeCodedValueId,
            s.Status.ToString(),
            s.DueDate,
            s.MaxScore,
            s.CreatedByTeacherId,
            s.CreatedAt,
            s.UpdatedAt)).ToArray();

        logger.LogInformation("Listed {Count} assignments", dtos.Length);
        return dtos;
    }
}