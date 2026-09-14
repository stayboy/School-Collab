using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;

/// <summary>
/// WS-C2 — a guardian e-signs a ward's submission (typed or click; spec §3.2
/// line 50). The handler validates state + guardian link, writes the append-only
/// <see cref="SignatureEvent"/> audit row and moves the submission to
/// <c>Signed</c> atomically. A second sign is a <c>409</c> and never writes a
/// second event (idempotent — NFR line 115).
/// </summary>
public sealed record SignOffSubmissionCommand(
    Guid AssignmentId,
    Guid StudentId,
    Guid GuardianId,
    SignatureType SignatureType,
    string? TypedSignature,
    string IpAddress,
    string UserAgent) : ICommand;
