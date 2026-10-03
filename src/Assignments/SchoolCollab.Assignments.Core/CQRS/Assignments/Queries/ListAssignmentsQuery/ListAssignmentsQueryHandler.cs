using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;

public sealed class ListAssignmentsQueryHandler(
    IAssignmentRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    IAssignmentPolicyResolver assignmentPolicyResolver,
    IFeatureFlagService featureFlags,
    ILogger<ListAssignmentsQueryHandler> logger) : IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// Per-(tenant, grade) effective-assignment-policy cache. The summary projection above is
    /// cached for five minutes, so resolving the policy once per <b>distinct</b> grade per list
    /// read is the difference between one cross-context fetch per row and one per grade. A policy
    /// change can take up to the cache lifetime to reach this surface (round risk R-B1-1);
    /// enforcement itself resolves fresh at publish time. Tagged <c>assignments</c> so a publish
    /// evicts it with the rest of the module's read cache.
    /// </summary>
    private static readonly HybridCacheEntryOptions PolicyCacheOptions = new()
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

        // The cached value is the TENANT-WIDE projection, deliberately: the key carries no scope
        // hash, so the caller's filter must be applied to what the cache RETURNS ([P1-3]).
        // Filtering inside the cached delegate would store one teacher's filtered list under the
        // tenant's key and serve it to every other teacher and admin in the tenant.
        var summaries = await cache.GetOrCreateAsync(
            cacheKey,
            (repository, query.Status, cache, assignmentPolicyResolver, featureFlags, tenantId, logger),
            static async (state, ct) =>
            {
                var (repo, status, hybridCache, policyResolver, flags, tenant, log) = state;
                var summaries = await repo.ListAsync(status, ct);

                // D3 / Q6: the derived approval field is the effective policy field OR'd with
                // FEATURE:RequireAssignmentApproval, resolved HERE so no UI surface reads the flag.
                // The flag leg sits in its own try/catch (plan-review P2-3): an unguarded call
                // would turn a Config outage into a 500 on the assignments list read, where the
                // replaced client-side read degraded to OFF. The resolver itself is fail-open, so
                // a failed policy fetch can only ever suppress the policy leg — never turn
                // approval ON.
                var flagOn = false;
                try
                {
                    flagOn = await flags.IsEnabledAsync(FeatureFlagKeys.RequireAssignmentApproval, ct);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Feature flag resolution failed; defaulting approval to OFF");
                }

                var approvalByGrade = await ResolveApprovalByGradeAsync(
                    summaries.Select(s => s.GradeLevelId), hybridCache, tenant, policyResolver, flagOn, ct);

                return summaries.Select(s => new AssignmentSummaryDto(
                    s.Id,
                    s.Title,
                    s.Description,
                    (AssignmentTypeDto)s.AssignmentType,
                    (GradingFormatDto)s.GradingFormat,
                    (TargetAudienceTypeDto)s.TargetAudienceType,
                    s.TopicId,
                    null,
                    s.GradeLevelId,
                    null,
                    (AssignmentStatusDto)s.Status,
                    s.DueDate,
                    s.MaxScore,
                    s.MandatoryReview,
                    s.CreatedByTeacherId,
                    s.CreatedAt,
                    s.UpdatedAt,
                    s.AvailableFromUtc,
                    s.ArchiveGraceDays,
                    (ApprovalStatusDto?)s.ApprovalStatus,
                    s.ApprovedBy,
                    s.ApprovedAt,
                    // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold +
                    // attempt cap projected alongside the existing
                    // lifecycle fields.
                    s.PassScore,
                    s.MaxAttempts,
                    // WS-C1 (spec §7 Q1): guardian-signature flag — must be
                    // mapped or every summary reports false.
                    s.RequiresSignature,
                    // WS-B2 (spec §3.4 line 70): per-difficulty counts — must be
                    // mapped or every list read resets them to null.
                    s.DifficultyEasyCount,
                    s.DifficultyMediumCount,
                    s.DifficultyHardCount,
                    // WS-E2b / ar-17: "has ever been published" for the failure surface.
                    PublishedAt: s.PublishedAt,
                    // D3 / Q6: the server-derived approval gate for this row.
                    RequiresApproval: approvalByGrade[GradeKey(s.GradeLevelId)],
                    // INS-1/INS-2 (assignment-authoring-compartments §9): student-facing
                    // text — must be mapped or every list read drops it.
                    Instructions: s.Instructions)).ToArray();
            },
            CacheOptions,
            tags: ["assignments"],
            cancellationToken: cancellationToken);

        return ApplyScope(summaries, query.Scope);
    }

    /// <summary>
    /// [P1-3]/D3 — the caller's visibility rule applied AFTER the cache read: a scoped teacher
    /// sees the rows they created plus the grades/subjects they teach; an unrestricted (or
    /// scope-less) caller sees the tenant-wide list unchanged.
    /// </summary>
    private static AssignmentSummaryDto[] ApplyScope(AssignmentSummaryDto[] summaries, TeacherScope? scope)
    {
        if (scope is null || scope.IsUnrestricted)
        {
            return summaries;
        }

        return summaries
            .Where(s => scope.Allows(s.CreatedByTeacherId, s.GradeLevelId, s.TopicId))
            .ToArray();
    }

    /// <summary>
    /// Resolves, per <b>distinct</b> <c>GradeLevelId</c> in the page (a null grade resolves the
    /// tenant default only), whether approval is required: the cached effective policy's
    /// <c>RequiresApprovalBeforePublish</c> OR'd with the already-resolved flag value.
    /// </summary>
    private static async Task<Dictionary<string, bool>> ResolveApprovalByGradeAsync(
        IEnumerable<Guid?> gradeLevelIds,
        HybridCache cache,
        Guid tenantId,
        IAssignmentPolicyResolver policyResolver,
        bool featureFlagOn,
        CancellationToken ct)
    {
        // Keyed by the same grade sentinel the policy cache key uses, because a null grade is a
        // legitimate key here and Dictionary<Guid?, bool> rejects a null key.
        var approvalByGrade = new Dictionary<string, bool>();

        foreach (var gradeLevelId in gradeLevelIds.Distinct())
        {
            var policy = await cache.GetOrCreateAsync(
                EffectivePolicyCacheKey(tenantId, gradeLevelId),
                (gradeLevelId, policyResolver),
                static async (state, token) =>
                {
                    var (grade, resolver) = state;
                    return await resolver.ResolveAsync(grade, token);
                },
                PolicyCacheOptions,
                tags: ["assignments"],
                cancellationToken: ct);

            approvalByGrade[GradeKey(gradeLevelId)] = policy.RequiresApprovalBeforePublish || featureFlagOn;
        }

        return approvalByGrade;
    }

    /// <summary>Per-(tenant, grade) effective-policy cache key; <c>none</c> is the sentinel for a
    /// null grade (the tenant default only).</summary>
    private static string EffectivePolicyCacheKey(Guid tenantId, Guid? gradeLevelId) =>
        $"assignment-policy:effective:{tenantId}:{GradeKey(gradeLevelId)}";

    /// <summary>The grade's sentinel key — <c>none</c> for a null grade (the tenant default
    /// only).</summary>
    private static string GradeKey(Guid? gradeLevelId) => gradeLevelId?.ToString() ?? "none";
}
