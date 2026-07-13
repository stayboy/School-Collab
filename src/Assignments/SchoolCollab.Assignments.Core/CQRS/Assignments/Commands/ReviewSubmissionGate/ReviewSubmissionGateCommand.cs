using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmissionGate;

/// <summary>
/// A Primary guardian reviews the submission gate for a student: approving
/// enables the student to self-submit (spec §4.10). Denying records the review
/// but leaves submission disabled.
/// </summary>
public sealed record ReviewSubmissionGateCommand(
    Guid GateId,
    Guid ReviewerGuardianId,
    bool Approve,
    string? Comment) : ICommand;
