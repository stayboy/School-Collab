using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.SetGradeLevelEnrollmentBlocked;

/// <summary>
/// Blocks (or unblocks) a grade level from being used for student enrollment.
/// Surfaced as a toggle on the Grade Levels landing page. Blocked grade levels
/// are excluded from the new-enrollment grade picker.
/// </summary>
public sealed record SetGradeLevelEnrollmentBlocked(Guid Id, bool Blocked) : ICommand;
