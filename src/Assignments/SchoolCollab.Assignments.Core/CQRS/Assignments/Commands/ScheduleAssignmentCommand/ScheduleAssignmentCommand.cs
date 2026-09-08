using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ScheduleAssignmentCommand;

/// <summary>Schedule an assignment to auto-publish at a future
/// moment (spec §3.5 step 2). The scheduled-publish sweep
/// dispatches <see cref="PublishAssignmentCommand"/> when
/// <see cref="AvailableFromUtc"/> arrives; the schedule command
/// itself does not snapshot recipients (decisions (f)/(g)).</summary>
public sealed record ScheduleAssignmentCommand(Guid AssignmentId, DateTimeOffset AvailableFromUtc) : ICommand;
