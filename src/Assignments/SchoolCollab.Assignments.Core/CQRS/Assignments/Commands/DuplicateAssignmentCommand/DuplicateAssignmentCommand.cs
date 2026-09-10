using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DuplicateAssignmentCommand;

/// <summary>Duplicate an existing assignment as a fresh Draft template copy
/// (WS-A4 / spec §3.1 — "Reuse/duplicate a past AR as a template"). The
/// handler clones the source's scalar fields + questions/options (with
/// re-pointed <c>CorrectOptionId</c>), attachments, modules, and resources;
/// it carries no reviews, recipients, gates, submissions, activity-group
/// links, approval stamps, or publish stamps. The copy is always a new
/// <see cref="SchoolCollab.Assignments.Core.Domain.AssignmentStatus.Draft"/>.</summary>
/// <param name="SourceId">The id of the assignment to duplicate. The source
/// is only read — never mutated.</param>
public sealed record DuplicateAssignmentCommand(Guid SourceId) : ICommand;
