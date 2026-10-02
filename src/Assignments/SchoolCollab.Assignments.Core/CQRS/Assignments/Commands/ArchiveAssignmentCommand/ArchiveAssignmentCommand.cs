using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ArchiveAssignmentCommand;

/// <summary>Archive an assignment (WS-A2 / spec §7 Q6 — read-only
/// retention). Dispatched by the archive sweep
/// (<see cref="SchoolCollab.Assignments.Api.Services.ArchiveSweeper"/>) and, from R1
/// (documents/specs/assignment-authoring-compartments.md §11 — the Closed row's primary
/// action), by <c>POST /assignments/{id}/archive</c>: the authoring page's explicit
/// retention action. The WS-A2 decision (f) kept the command sweep-only; R1 adds the
/// author-facing route.</summary>
public sealed record ArchiveAssignmentCommand(Guid AssignmentId) : ICommand;
