using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmission;

/// <summary>
/// A teacher reviews a student's submission (spec §4.13), recording a score /
/// grade / comments and flipping the submission's <c>ReviewState</c> to
/// Reviewed (or Graded when a score/grade is present).
/// </summary>
public sealed record ReviewSubmissionCommand(
    Guid SubmissionId,
    Guid TeacherId,
    decimal? Score,
    string? Grade,
    string? Comments) : ICommand;
