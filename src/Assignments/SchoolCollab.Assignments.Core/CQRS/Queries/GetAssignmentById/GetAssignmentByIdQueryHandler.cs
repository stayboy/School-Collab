using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Core.Queries.GetAssignmentById;

public sealed class GetAssignmentByIdQueryHandler(
    AssignmentsDbContext db,
    HybridCache cache,
    ILogger<GetAssignmentByIdQueryHandler> logger) : IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<AssignmentSummaryDto?> HandleAsync(
        GetAssignmentByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling GetAssignmentByIdQuery {Id}", query.Id);

        var cacheKey = $"assignment:{query.Id}:{db.CurrentTenantId}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, query.Id),
            static async (state, ct) =>
            {
                var (dbContext, id) = state;
                var assignment = await dbContext.Assignments
                    .AsNoTracking()
                    .SingleOrDefaultAsync(a => a.Id == id, ct);

                if (assignment is null)
                    return null;

                return new AssignmentSummaryDto(
                    assignment.Id,
                    assignment.Title,
                    assignment.Description,
                    (AssignmentTypeDto)assignment.AssignmentType,
                    (GradingFormatDto)assignment.GradingFormat,
                    (TargetAudienceTypeDto)assignment.TargetAudienceType,
                    assignment.SubjectCodedValueId,
                    null,
                    assignment.GradeCodedValueId,
                    null,
                    (AssignmentStatusDto)assignment.Status,
                    assignment.DueDate,
                    assignment.MaxScore,
                    assignment.CreatedByTeacherId,
                    assignment.CreatedAt,
                    assignment.UpdatedAt);
            },
            CacheOptions,
            tags: ["assignments"],
            cancellationToken: cancellationToken);
    }
}
