using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentOnBehalf;

/// <summary>
/// A guardian submits an assignment on behalf of their ward (spec §4.7 / §4.11).
/// Requires an existing, enabled <see cref="SchoolCollab.Assignments.Core.Domain.GuardianSubmissionGate"/>.
/// <para>WS-A3 (spec §3.3): the handler scores the inbound
/// <see cref="Answers"/> via the pure <c>IScoringEngine</c>, persists
/// per-version answer rows, and returns void (the on-behalf surface is
/// NoContent — parity with the existing flow).</para>
/// </summary>
public sealed record SubmitAssignmentOnBehalfCommand(
    Guid AssignmentId,
    Guid StudentId,
    Guid GuardianId,
    string? Content,
    IReadOnlyList<SubmissionAnswerDto>? Answers = null) : ICommand;
