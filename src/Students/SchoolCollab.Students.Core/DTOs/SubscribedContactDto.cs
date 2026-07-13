using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Cross-bounded-context contract (spec §9, G5) returned by
/// <c>GET /contacts/subscribed</c> and consumed by the Assignments resolver
/// (Phase 6). <see cref="Role"/> is populated for guardian-owned contacts
/// (null for student-owned) from the guardian's student-guardian link;
/// it lets the resolver distinguish Primary (review + submit-on-behalf) from
/// CC (read-only + broadcast).
/// </summary>
public sealed record SubscribedContactDto(
    Guid Id,
    ContactChannel Channel,
    string Value,
    GuardianRole? Role);
