using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateStudentSubmission;

/// <summary>
/// A student submits / resubmits their own assignment (spec §4.7 / §4.11).
/// Gated by <c>Assignment.MandatoryReview</c>: allowed only when the assignment
/// does not mandate review, or the <see cref="SchoolCollab.Assignments.Core.Domain.GuardianSubmissionGate"/>
/// has been enabled by a Primary guardian review (§4.10). Inserts a new
/// <see cref="SchoolCollab.Assignments.Core.Domain.AssignmentSubmissionVersion"/>
/// and bumps CurrentVersionNumber.
/// <para>WS-A3 (spec §3.3): the handler scores the inbound
/// <see cref="Answers"/> via the pure <c>IScoringEngine</c>, persists
/// per-version answer rows, and returns <c>SubmissionFeedbackDto</c> for
/// InstantGraded (null otherwise — AutoGraded/TeacherGraded return
/// 204 No Content).</para>
/// </summary>
public sealed record CreateStudentSubmissionCommand(
    Guid AssignmentId,
    Guid StudentId,
    string? Content,
    IReadOnlyList<SubmissionAnswerDto>? Answers = null) : ICommand;