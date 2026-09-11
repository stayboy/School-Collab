using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;

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
                    assignment.TopicId,
                    null,
                    assignment.GradeLevelId,
                    null,
                    (AssignmentStatusDto)assignment.Status,
                    assignment.DueDate,
                    assignment.MaxScore,
                    assignment.MandatoryReview,
                    assignment.CreatedByTeacherId,
                    assignment.CreatedAt,
                    assignment.UpdatedAt,
                    assignment.AvailableFromUtc,
                    assignment.ArchiveGraceDays,
                    (ApprovalStatusDto?)assignment.ApprovalStatus,
                    assignment.ApprovedBy,
                    assignment.ApprovedAt,
                    // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold +
                    // attempt cap projected alongside the existing
                    // lifecycle fields.
                    assignment.PassScore,
                    assignment.MaxAttempts,
                    // WS-C1 (spec §7 Q1): guardian-signature flag — must be
                    // mapped or every read reports false.
                    assignment.RequiresSignature);
            },
            CacheOptions,
            tags: ["assignments"],
            cancellationToken: cancellationToken);
    }
}
