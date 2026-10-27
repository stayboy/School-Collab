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
/// <para>Round notification-templates-b1 (§3.4): the same <c>contacts/subscribed</c> response that
/// carries a subscriber's channel/role now also carries <c>ContactName</c>/<c>StudentName</c>, which
/// ride into <see cref="SubscriberInfo"/> — name enrichment costs no extra hop, and each name
/// degrades to <see cref="string.Empty"/> when the Students side does not know it.</para>
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

        // FR-ER-13/15: drop the cohort members who are in a targeted grade but not enrolled in
        // the subject. Runs before the guardian/teacher/subscribed legs so a filtered student
        // contributes no owners at all.
        studentIds = await ExcludeUnenrolledAsync(client, request, studentIds, cancellationToken);

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
                // The ward id rides the call (§3.4) so a guardian linked to several students
                // gets the name of the ward THIS recipient row is for. Still one call — the
                // name enrichment costs no extra hop.
                var wardQuery = studentId is null ? string.Empty : $"&studentId={studentId}";
                contacts = await client.GetFromJsonAsync<SubscribedContactDto[]>(
                    $"contacts/subscribed?ownerType={(int)ownerType}&ownerId={ownerId}&scope={(int)request.Scope}{wardQuery}",
                    cancellationToken)
                    ?? [];
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Failed to resolve subscribed contacts for {OwnerType} {OwnerId}", ownerType, ownerId);
                contacts = [];
            }

            foreach (var c in contacts)
                result.Add(new SubscriberInfo(
                    c.Id, ownerType, ownerId, studentId, c.Channel, c.Role,
                    ContactName: c.ContactName ?? string.Empty,
                    StudentName: c.StudentName ?? string.Empty));
        }

        return result;
    }

    /// <summary>
    /// FR-ER-13/13a/13b/15 — the subject-enrolment filter over the resolved publish cohort.
    /// <para><b>Rule 1:</b> no subject (<see cref="ResolveSubscribersRequest.TopicId"/> is null)
    /// means the publish is not subject-scoped: no hop at all, cohort unchanged.</para>
    /// <para><b>Rule 2:</b> with a subject, eligibility is resolved <b>per grade</b> in
    /// <see cref="ResolveSubscribersRequest.GradeLevelIds"/>, one call each.</para>
    /// <para><b>Rule 3:</b> a student is removed only when they are in a targeted grade's
    /// <c>Audience</c> and absent from that grade's <c>Eligible</c> set — so a student who came
    /// in on a stream/student/all-students/activity-group leg (absent from every audience) is
    /// never touched. Enforcing on grades only is spec decision 10; over-filtering a mixed
    /// cohort is the silent truncation decision 12 accepts.</para>
    /// <para><b>Rule 4:</b> no grade scope means nothing to subtract — the cohort is unchanged.</para>
    /// <para><b>Fail-open is per grade (FR-ER-15):</b> a failed call logs and contributes no
    /// exclusions for that grade, while the other grades' answers still apply. A transient
    /// error must never withhold work from a whole grade.</para>
    /// </summary>
    private async Task<List<Guid>> ExcludeUnenrolledAsync(
        HttpClient client,
        ResolveSubscribersRequest request,
        List<Guid> studentIds,
        CancellationToken cancellationToken)
    {
        if (request.TopicId is not { } topicId) return studentIds;

        var gradeIds = (request.GradeLevelIds ?? []).Distinct().ToArray();
        if (gradeIds.Length == 0) return studentIds;

        var excluded = new HashSet<Guid>();
        foreach (var gradeId in gradeIds)
        {
            TopicEnrollmentDto? enrollment;
            try
            {
                // No period parameter: the academic year is resolved server-side (FR-ER-3).
                enrollment = await client.GetFromJsonAsync<TopicEnrollmentDto>(
                    $"students/topic-enrollment?gradeLevelId={gradeId}&topicId={topicId}",
                    cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex,
                    "Failed to resolve subject enrollment for grade {GradeId} / topic {TopicId}; the grade's recipients stay unfiltered",
                    gradeId, topicId);
                continue;
            }

            if (enrollment is null) continue;

            var eligible = enrollment.Eligible.ToHashSet();
            foreach (var studentId in enrollment.Audience)
            {
                if (!eligible.Contains(studentId)) excluded.Add(studentId);
            }
        }

        return excluded.Count == 0
            ? studentIds
            : studentIds.Where(id => !excluded.Contains(id)).ToList();
    }
}
