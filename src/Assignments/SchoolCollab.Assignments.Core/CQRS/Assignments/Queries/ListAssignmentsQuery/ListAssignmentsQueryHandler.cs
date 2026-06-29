using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;

public sealed class ListAssignmentsQueryHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<ListAssignmentsQueryHandler> logger) : IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<AssignmentSummaryDto[]> HandleAsync(
        ListAssignmentsQuery query,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ListAssignmentsQuery with status {Status}", query.Status?.ToString() ?? "all");

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var cacheKey = $"assignments:list:{tenantId}:{query.Status?.ToString() ?? "all"}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (repository, query.Status),
            static async (state, ct) =>
            {
                var (repo, status) = state;
                var summaries = await repo.ListAsync(status, ct);

                return summaries.Select(s => new AssignmentSummaryDto(
                    s.Id,
                    s.Title,
                    s.Description,
                    (AssignmentTypeDto)s.AssignmentType,
                    (GradingFormatDto)s.GradingFormat,
                    (TargetAudienceTypeDto)s.TargetAudienceType,
                    s.SubjectCodedValueId,
                    null,
                    s.GradeCodedValueId,
                    null,
                    (AssignmentStatusDto)s.Status,
                    s.DueDate,
                    s.MaxScore,
                    s.CreatedByTeacherId,
                    s.CreatedAt,
                    s.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["assignments"],
            cancellationToken: cancellationToken);
    }
}
