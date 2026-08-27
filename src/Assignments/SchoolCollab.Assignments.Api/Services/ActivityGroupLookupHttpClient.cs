using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="IActivityGroupLookup"/> (spec activity-group-enrollment.md
/// FR-20..22, EC-4/EC-11). Calls the Students API via Aspire service discovery
/// (named client <c>students-api</c>). The Students API is tenant-scoped, so a
/// group from another tenant or a missing group returns null and is omitted —
/// callers reject omitted ids (FR-21, EC-11).
/// </summary>
public sealed class ActivityGroupLookupHttpClient(
    IHttpClientFactory httpClientFactory,
    ILogger<ActivityGroupLookupHttpClient> logger) : IActivityGroupLookup
{
    public async Task<ActivityGroupRefDto[]> GetByIdsAsync(
        IReadOnlyList<Guid> activityGroupIds, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("students-api");
        var result = new List<ActivityGroupRefDto>();

        foreach (var id in activityGroupIds.Distinct())
        {
            ActivityGroupDto? group;
            try
            {
                group = await client.GetFromJsonAsync<ActivityGroupDto>(
                    $"activity-groups/{id}", cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Failed to look up activity group {Id}", id);
                continue;
            }

            if (group is not null)
                result.Add(new ActivityGroupRefDto(group.Id, group.Name, group.IsActive));
        }

        return result.ToArray();
    }

    public async Task<Guid[]> GetActiveMemberIdsAsync(
        IReadOnlyList<Guid> activityGroupIds, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("students-api");
        var result = new List<Guid>();

        foreach (var id in activityGroupIds.Distinct())
        {
            // EC-4: archived groups are excluded from recipient resolution.
            ActivityGroupDto? group;
            try
            {
                group = await client.GetFromJsonAsync<ActivityGroupDto>(
                    $"activity-groups/{id}", cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Failed to look up activity group {Id}", id);
                continue;
            }
            if (group is null || !group.IsActive)
                continue;

            MembershipDto[] members;
            try
            {
                members = await client.GetFromJsonAsync<MembershipDto[]>(
                    $"activity-groups/{id}/members", cancellationToken) ?? [];
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Failed to resolve members for activity group {Id}", id);
                continue;
            }

            result.AddRange(members
                .Where(m => m.Status == "Active")
                .Select(m => m.StudentId));
        }

        return result.Distinct().ToArray();
    }
}
