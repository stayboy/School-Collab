using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateStudentSubmission;

/// <summary>
/// A student submits / resubmits their own assignment (spec §4.7 / §4.11).
/// Gated by <c>Assignment.MandatoryReview</c>: allowed only when the assignment
/// does not mandate review, or the <see cref="SchoolCollab.Assignments.Core.Domain.GuardianSubmissionGate"/>
/// has been enabled by a Primary guardian review (§4.10). Inserts a new
/// <see cref="SchoolCollab.Assignments.Core.Domain.AssignmentSubmissionVersion"/>
/// and bumps CurrentVersionNumber.
/// </summary>
public sealed record CreateStudentSubmissionCommand(
    Guid AssignmentId,
    Guid StudentId,
    string? Content) : ICommand;