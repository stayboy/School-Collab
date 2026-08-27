using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="ITopicAssignmentLookup"/> (spec activity-group-enrollment.md
/// FR-58). Calls the Students API via Aspire service discovery (named client
/// <c>students-api</c>): for a SelectedGrades assignment it checks
/// <c>GET /students/topic-assignments/by-grade/{gradeId}?effectiveDate=…</c>; for
/// SelectedGroups it checks each linked group via
/// <c>/by-activity-group/{groupId}?effectiveDate=…</c>. The Students endpoint
/// already filters by the topic assignment's effective [StartDate, EndDate]
/// window (date-based or period-aligned via the Rev. 6 PeriodId), so a matching
/// <c>TopicId</c> in the response means the subject is assigned for a period
/// covering the effective date. Fails open (treats as assigned) if Students is
/// unreachable — this is a publish-time validation refinement, not a hard gate.
/// </summary>
public sealed class TopicAssignmentLookupHttpClient(
    IHttpClientFactory httpClientFactory,
    ILogger<TopicAssignmentLookupHttpClient> logger) : ITopicAssignmentLookup
{
    public async Task<bool> IsTopicAssignedAsync(
        Guid? gradeLevelId,
        IReadOnlyList<Guid> activityGroupIds,
        Guid topicId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("students-api");

        if (gradeLevelId is { } gradeId)
            return await IsAssignedToGradeAsync(client, gradeId, topicId, effectiveDate, cancellationToken);

        var assigned = true;
        foreach (var groupId in activityGroupIds.Distinct())
        {
            if (!await IsAssignedToGroupAsync(client, groupId, topicId, effectiveDate, cancellationToken))
                assigned = false;
        }
        return assigned;
    }

    private async Task<bool> IsAssignedToGradeAsync(
        HttpClient client, Guid gradeId, Guid topicId, DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var dtos = await client.GetFromJsonAsync<TopicAssignmentDto[]>(
                $"topic-assignments/by-grade/{gradeId}?effectiveDate={date:yyyy-MM-dd}", cancellationToken);
            return dtos?.Any(d => d.TopicId == topicId) ?? false;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Students API unreachable checking grade topic assignment; failing open");
            return true;
        }
    }

    private async Task<bool> IsAssignedToGroupAsync(
        HttpClient client, Guid groupId, Guid topicId, DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var dtos = await client.GetFromJsonAsync<TopicAssignmentDto[]>(
                $"topic-assignments/by-activity-group/{groupId}?effectiveDate={date:yyyy-MM-dd}", cancellationToken);
            return dtos?.Any(d => d.TopicId == topicId) ?? false;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Students API unreachable checking group topic assignment; failing open");
            return true;
        }
    }
}