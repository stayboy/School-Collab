using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RecordModuleProgress;

/// <summary>
/// WS-D1 (spec §3.3) — a ward reports progress on one content module of an
/// assignment (video watch % / guide scroll-complete %). Upserts the
/// monotonic + idempotent <see cref="SchoolCollab.Assignments.Core.Domain.ModuleProgress"/>
/// row; the handler returns <see langword="false"/> when the module is not on
/// the assignment (route 404) and throws
/// <see cref="SchoolCollab.Assignments.Core.Domain.Exceptions.AssignmentNotFoundException"/>
/// when the assignment is absent.
/// </summary>
public sealed record RecordModuleProgressCommand(
    Guid AssignmentId,
    Guid StudentId,
    Guid ContentModuleId,
    int Percent) : ICommand;
