namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Read-only projection of a <see cref="Domain.ContactAuditEntry"/> for the
/// student/guardian contact-history surfaces.
/// </summary>
public sealed record ContactAuditEntryDto(
    Guid Id,
    Guid ContactId,
    string ChangeKind,
    string? PreviousChannel,
    string PreviousValue,
    string? PreviousLabel,
    string? PreviousCountryCode,
    string? NewChannel,
    string? NewValue,
    string? NewLabel,
    string? NewCountryCode,
    string Reason,
    string ActorId,
    string ActorDisplayName,
    DateTimeOffset OccurredAt);
