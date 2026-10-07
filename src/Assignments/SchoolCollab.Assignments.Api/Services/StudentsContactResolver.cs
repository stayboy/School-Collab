using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="IContactResolver"/> (spec §9 G5 / Phase 6). Turns the publish cohort —
/// the student ids the target resolver already produced, plus each targeted grade's teachers —
/// into guardians / owners and then each owner's subscribed contacts, returning a flat subscriber
/// list the publish handler can turn into <c>AssignmentRecipient</c> rows.
/// The named HttpClient <c>students-api</c> is resolved through Aspire service discovery (AppHost
/// wires <c>assignments-api</c> → <c>students-api</c>).
/// <para>Round <c>drop-primary-grade</c>: the <c>students/by-grade/{id}</c> whole-roster fallback is
/// gone — publish resolves its students from the authored targets and refuses an empty set before
/// reaching this resolver, so the by-grade path was dead. The teacher-cohort leg now runs once per
/// distinct grade target (fail-open per grade).</para>
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

        var owners = new List<(ContactOwnerType OwnerType, Guid OwnerId, Guid? StudentId)>();
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

        // Teachers are now notification recipients (dm/2 reverses the v1
        // "teachers not notification recipients" carve-out). The cohort carries the teachers of
        // EVERY targeted grade (round drop-primary-grade — the authored grade targets are the only
        // grade source). Their contacts have no ward student, so StudentId is null. Each grade is
        // fail-open (the ADR's graceful-degradation posture): a grade whose teachers cannot be read
        // contributes none and never fails the publish.
        foreach (var gradeId in (request.GradeLevelIds ?? []).Distinct())
        {
            TeacherWithRoleDto[] gradeTeachers = [];
            try
            {
                gradeTeachers = await client.GetFromJsonAsync<TeacherWithRoleDto[]>(
                    $"grade-levels/{gradeId}/teachers", cancellationToken)
                    ?? [];
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Failed to resolve teachers for grade {GradeId}", gradeId);
            }

            foreach (var t in gradeTeachers)
                owners.Add((ContactOwnerType.Teacher, t.Id, null));
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
