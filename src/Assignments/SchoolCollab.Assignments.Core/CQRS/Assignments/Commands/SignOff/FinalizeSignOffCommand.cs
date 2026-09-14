using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;

/// <summary>
/// WS-C1/C4 — a teacher finalizes a signed sign-off (spec §3.2 line 51),
/// stamping <c>FinalizedAt</c> (the terminal locked marker). Teacher action only
/// in v1; the "auto on sign" variant is recorded backlog. <c>TeacherId</c> is not
/// captured (the D-6 <c>Guid.Empty</c> posture, recorded).
/// </summary>
public sealed record FinalizeSignOffCommand(Guid AssignmentId, Guid StudentId) : ICommand;