using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Core.CQRS.GradeStreams.Queries.ListGradeStreams;

/// <summary>
/// Resolves the grade's stream bridge rows against the <c>GRSTREAMS</c>
/// catalogue in ONE override-resolving hop
/// (<see cref="ICodedValuesApiClient.GetChildrenByParentCodeAsync"/>), so the
/// returned <see cref="GradeStreamDto"/> carries the tenant-resolved name as
/// well as the override metadata. Bridge ids missing from the catalogue (a
/// deleted coded value) are skipped rather than rendered as a blank row.
/// </summary>
public sealed class ListGradeStreamsHandler(
    IGradeStreamAssignmentRepository repository,
    ICodedValuesApiClient codedValuesApi,
    ILogger<ListGradeStreamsHandler> logger) : IQueryHandler<ListGradeStreams, GradeStreamDto[]>
{
    public async Task<GradeStreamDto[]> HandleAsync(
        ListGradeStreams query, CancellationToken cancellationToken = default)
    {
        var assignments = await repository.ListByGradeLevelAsync(query.GradeLevelId, cancellationToken);
        if (assignments.Length == 0) return [];

        var catalogue = await codedValuesApi.GetChildrenByParentCodeAsync(
            GradeStreamAssignment.CatalogueParentCode, cancellationToken);
        var byId = catalogue.ToDictionary(x => x.Id);

        var streams = new List<GradeStreamDto>(assignments.Length);
        foreach (var assignment in assignments)
        {
            if (!byId.TryGetValue(assignment.StreamCodedValueId, out var cv))
            {
                logger.LogWarning(
                    "Grade {GradeLevelId} offers stream coded value {StreamCodedValueId} but it is not in the GRSTREAMS catalogue; skipping",
                    query.GradeLevelId, assignment.StreamCodedValueId);
                continue;
            }

            streams.Add(new GradeStreamDto(
                assignment.Id,
                cv.Id,
                assignment.GradeLevelId,
                cv.Code,
                cv.Name,
                cv.DefaultName,
                cv.Description,
                cv.Attributes.FirstOrDefault(a => a.Key == GradeStreamAssignment.StreamVersionAttributeKey)?.Value,
                cv.IsOverridden,
                cv.IsDisabled,
                cv.DisplayOrder));
        }

        return [.. streams];
    }
}
