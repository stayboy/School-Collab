using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="IContactResolver"/> (spec §9 G5 / Phase 6). Enumerates
/// the publish cohort via the Students API — students by grade, their guardians,
/// and each owner's subscribed contacts — and returns a flat subscriber list the
/// publish handler can turn into <c>AssignmentRecipient</c> rows.
/// The named HttpClient <c>students-api</c> is resolved through Aspire service
/// discovery (AppHost wires <c>assignments-api</c> → <c>students-api</c>).
/// </summary>
public sealed class StudentsContactResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<StudentsContactResolver> logger) : IContactResolver
{
    public async Task<IReadOnlyList<SubscriberInfo>> ResolveSubscribersAsync(
        ResolveSubscribersRequest request, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("students-api");
        var studentIds = (request.StudentIds ?? []).ToList();

        if (studentIds.Count == 0 && request.GradeLevelId.HasValue)
        {
            var byGrade = await client.GetFromJsonAsync<SchoolCollab.Students.Core.DTOs.StudentDto[]>(
                $"students/by-grade/{request.GradeLevelId.Value}", cancellationToken)
                ?? [];
            studentIds.AddRange(byGrade.Select(s => s.Id));
        }

        var owners = new List<(ContactOwnerType OwnerType, Guid OwnerId, Guid StudentId)>();
        foreach (var studentId in studentIds)
        {
            owners.Add((ContactOwnerType.Student, studentId, studentId));

            StudentGuardianViewDto[] guardians;
            try
            {
                guardians = await client.GetFromJsonAsync<StudentGuardianViewDto[]>(
                    $"students/{studentId}/guardians", cancellationToken)
                    ?? [];
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Failed to resolve guardians for student {StudentId}", studentId);
                guardians = [];
            }

            foreach (var g in guardians)
                owners.Add((ContactOwnerType.Guardian, g.GuardianId, g.StudentId));
        }

        var result = new List<SubscriberInfo>();
        foreach (var (ownerType, ownerId, studentId) in owners)
        {
            SubscribedContactDto[] contacts;
            try
            {
                contacts = await client.GetFromJsonAsync<SubscribedContactDto[]>(
                    $"contacts/subscribed?ownerType={(int)ownerType}&ownerId={ownerId}&scope={(int)request.Scope}",
                    cancellationToken)
                    ?? [];
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Failed to resolve subscribed contacts for {OwnerType} {OwnerId}", ownerType, ownerId);
                contacts = [];
            }

            foreach (var c in contacts)
                result.Add(new SubscriberInfo(c.Id, ownerType, ownerId, studentId, c.Channel, c.Role));
        }

        return result;
    }
}
