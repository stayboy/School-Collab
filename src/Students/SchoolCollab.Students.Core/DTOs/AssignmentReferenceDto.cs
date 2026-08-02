namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// A lightweight summary of an assignment that references an activity group,
/// returned by <see cref="SchoolCollab.Students.Core.Services.IActivityGroupAssignmentQuery"/>.
/// Used by the delete-guard to report which assignments block the delete.
/// </summary>
public sealed record AssignmentReferenceDto(
    Guid Id,
    string Title,
    string Status);
