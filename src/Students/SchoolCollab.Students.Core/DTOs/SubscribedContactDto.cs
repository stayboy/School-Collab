using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Cross-bounded-context contract (spec §9, G5) returned by
/// <c>GET /contacts/subscribed</c> and consumed by the Assignments resolver
/// (Phase 6). <see cref="Role"/> is populated for guardian-owned contacts
/// (null for student-owned) from the guardian's student-guardian link;
/// it lets the resolver distinguish Primary (review + submit-on-behalf) from
/// CC (read-only + broadcast).
/// <para>Round notification-templates-b1 (§3.4 name enrichment): <see cref="ContactName"/>
/// and <see cref="StudentName"/> ride the same response so the notification renderer's
/// <c>{{GuardianName}}</c> / <c>{{StudentName}}</c> tags carry real values with <b>no second
/// hop</b> — both are populated from rows this handler already reads. They are
/// <see langword="null"/> when the name is absent (a missing guardian row, a student-owned
/// contact with no ward passed in, a teacher contact), and the consumer degrades to
/// <see cref="string.Empty"/>; enrichment never fails the parent operation.</para>
/// </summary>
public sealed record SubscribedContactDto(
    Guid Id,
    ContactChannel Channel,
    string Value,
    GuardianRole? Role,
    string? ContactName = null,
    string? StudentName = null);
