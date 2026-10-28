using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.DTOs;

/// <summary>
/// One recipient-linked assignment, reduced to the flags the stats fold consumes
/// (<c>GetStudentAssignmentStatsQueryHandler</c>) — decision A(ii) of round
/// <c>student-stats-l2a</c>: the arithmetic lives in testable C#, not in a SQL <c>GROUP BY</c>.
/// <para>
/// The read that produces these rows carries <b>no status predicate</b> — recipient link only. The
/// sent test (Q126: <c>Published</c>, or <c>Scheduled</c> whose publish time has passed) is a fold rule,
/// so a <c>Draft</c> row and a not-yet-active <c>Scheduled</c> row must reach the handler. Filtering the
/// statuses in the WHERE clause would make <c>Assigned</c> equal <c>SentOut</c> and let the status rules
/// pass vacuously.
/// </para>
/// </summary>
/// <param name="Status">The assignment's lifecycle status — the raw input to the sent test.</param>
/// <param name="AvailableFromUtc">The scheduled publish moment; only meaningful for
/// <see cref="AssignmentStatus.Scheduled"/>.</param>
/// <param name="MandatoryReview">Whether every send out of this assignment is gated on guardian review.</param>
/// <param name="HasFinalizedSubmission">Whether this student's submission carries <c>FinalizedAt</c>.</param>
/// <param name="ReviewState">That same submission's review state, null when no submission exists. Read
/// together with <paramref name="HasFinalizedSubmission"/>: <c>Graded</c> is reachable without a
/// finalization (<c>ApplyReview</c> is independent of the sign-off chain), so grading alone must never
/// count as marked.</param>
/// <param name="HasReviewedGate">Whether this student's <c>GuardianSubmissionGate</c> row carries
/// <c>ReviewedAt</c>. Folded to a bool by the read so the handler never reasons about row existence.</param>
public record StudentAssignmentStatRow(
    AssignmentStatus Status,
    DateTimeOffset? AvailableFromUtc,
    bool MandatoryReview,
    bool HasFinalizedSubmission,
    ReviewState? ReviewState,
    bool HasReviewedGate);
