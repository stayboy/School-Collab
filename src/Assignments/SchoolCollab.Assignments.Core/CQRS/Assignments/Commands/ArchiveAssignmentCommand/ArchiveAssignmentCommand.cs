using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ArchiveAssignmentCommand;

/// <summary>Archive an assignment (WS-A2 / spec §7 Q6 — read-only
/// retention). Dispatched ONLY by the archive sweep
/// (<see cref="SchoolCollab.Assignments.Api.Services.ArchiveSweeper"/>)
/// — no public API route exists for this command (decision (f)).</summary>
public sealed record ArchiveAssignmentCommand(Guid AssignmentId) : ICommand;
