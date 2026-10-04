using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;

public sealed class GetAssignmentByIdQueryHandler(
    AssignmentsDbContext db,
    HybridCache cache,
    IAssignmentPolicyResolver assignmentPolicyResolver,
    IFeatureFlagService featureFlags,
    ILogger<GetAssignmentByIdQueryHandler> logger) : IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    /// <summary>Per-(tenant, grade) effective-assignment-policy cache — see
    /// <c>ListAssignmentsQueryHandler.PolicyCacheOptions</c> for the rationale (risk R-B1-1).</summary>
    private static readonly HybridCacheEntryOptions PolicyCacheOptions = new()
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

        // The cached value is the tenant-wide row, deliberately — the scope check is applied to
        // what the cache RETURNS ([P2-2], the same discipline as the list read's [P1-3]).
        var summary = await cache.GetOrCreateAsync(
            cacheKey,
            (db, query.Id, cache, assignmentPolicyResolver, featureFlags, logger),
            static async (state, ct) =>
            {
                var (dbContext, id, hybridCache, policyResolver, flags, log) = state;
                var assignment = await dbContext.Assignments
                    .AsNoTracking()
                    .SingleOrDefaultAsync(a => a.Id == id, ct);

                if (assignment is null)
                    return null;

                // D3 / Q6: the derived approval field is the effective policy field OR'd with
                // FEATURE:RequireAssignmentApproval, resolved HERE so the detail surface reads no
                // flag itself. The flag leg is guarded (plan-review P2-3) so a Config outage
                // degrades to OFF — the replaced client-side read's behaviour — instead of 500-ing
                // the detail read. The resolver is fail-open, so a failed policy fetch can never
                // turn approval ON.
                var flagOn = false;
                try
                {
                    flagOn = await flags.IsEnabledAsync(FeatureFlagKeys.RequireAssignmentApproval, ct);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Feature flag resolution failed; defaulting approval to OFF");
                }

                var policy = await hybridCache.GetOrCreateAsync(
                    EffectivePolicyCacheKey(dbContext.CurrentTenantId, assignment.GradeLevelId),
                    (assignment.GradeLevelId, policyResolver),
                    static async (policyState, token) =>
                    {
                        var (grade, resolver) = policyState;
                        return await resolver.ResolveAsync(grade, token);
                    },
                    PolicyCacheOptions,
                    tags: ["assignments"],
                    cancellationToken: ct);

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
                    assignment.RequiresSignature,
                    // WS-B2 (spec §3.4 line 70): per-difficulty counts — must be
                    // mapped or every detail read resets them to null.
                    assignment.DifficultyEasyCount,
                    assignment.DifficultyMediumCount,
                    assignment.DifficultyHardCount,
                    // WS-E2b / ar-17: "has ever been published" for the failure surface.
                    PublishedAt: assignment.PublishedAt,
                    // D3 / Q6: the server-derived approval gate for this assignment.
                    RequiresApproval: policy.RequiresApprovalBeforePublish || flagOn,
                    // INS-1/INS-2 (assignment-authoring-compartments §9): student-facing
                    // text — must be mapped or every detail read drops it.
                    Instructions: assignment.Instructions);
            },
            CacheOptions,
            tags: ["assignments"],
            cancellationToken: cancellationToken);

        // [P2-2] an out-of-scope id is indistinguishable from an unknown id (404) — the caller
        // must not learn that the assignment exists.
        if (summary is null)
        {
            return null;
        }

        if (query.Scope is { IsUnrestricted: false } scope
            && !scope.Allows(summary.CreatedByTeacherId, summary.GradeLevelId, summary.TopicId))
        {
            return null;
        }

        return summary;
    }

    /// <summary>Per-(tenant, grade) effective-policy cache key; <c>none</c> is the sentinel for a
    /// null grade (the tenant default only).</summary>
    private static string EffectivePolicyCacheKey(Guid tenantId, Guid? gradeLevelId) =>
        $"assignment-policy:effective:{tenantId}:{gradeLevelId?.ToString() ?? "none"}";
}
