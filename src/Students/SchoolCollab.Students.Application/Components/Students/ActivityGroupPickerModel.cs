namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>
/// Input for <see cref="ActivityGroupPickerDialog"/>: whose memberships are being picked, and the grade the
/// eligibility check runs against (their active enrollment's grade — null when they have none, which blocks
/// every grade-restricted group).
/// </summary>
public sealed record ActivityGroupPickerModel(Guid StudentId, Guid? GradeLevelId);
