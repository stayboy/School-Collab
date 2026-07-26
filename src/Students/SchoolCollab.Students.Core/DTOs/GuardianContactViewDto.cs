namespace SchoolCollab.Students.Core.DTOs;

using SchoolCollab.Students.Core.Domain;

/// <summary>
/// Lightweight contact summary used inside guardian list / link DTOs.
/// Carries just enough information to render a contact line in a grid or
/// pill without the full <see cref="ContactDto"/> payload.
/// </summary>
public sealed record GuardianContactViewDto(
    ContactChannel Channel,
    string Value,
    string? CountryCode = null);
