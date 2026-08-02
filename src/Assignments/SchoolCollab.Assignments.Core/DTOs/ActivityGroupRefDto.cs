namespace SchoolCollab.Assignments.Core.DTOs;

/// <summary>
/// A lightweight group reference returned by the Assignments API link endpoints
/// (spec §7.3 <c>ActivityGroupRefDto</c>).
/// </summary>
public sealed record ActivityGroupRefDto(
    Guid Id,
    string Name,
    string Status);
