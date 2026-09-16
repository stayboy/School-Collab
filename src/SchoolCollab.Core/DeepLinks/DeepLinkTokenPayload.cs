namespace SchoolCollab.Core.DeepLinks;

/// <summary>
/// The protected payload inside a contact-scoped deep-link token (ar-14-deep-links).
/// Serialised to JSON and wrapped by <see cref="DeepLinkProtector"/> with the shared
/// <see cref="DeepLinkConstants.Purpose"/> purpose. The minting host (Assignments API)
/// and the validating host (Families) exchange this exact shape.
/// <para><see cref="OwnerType"/> and <see cref="Role"/> are carried as raw ints so the
/// Core contract (which may not reference <c>Students.Core</c>) can model them without
/// a type dependency; the Families landing maps them to the Students enums at the edge.
/// </para>
/// </summary>
public sealed record DeepLinkTokenPayload(
    Guid TenantId,
    Guid AssignmentId,
    Guid ContactId,
    int OwnerType,
    int? Role,
    Guid? WardStudentId,
    DateTimeOffset ExpiresAt);
