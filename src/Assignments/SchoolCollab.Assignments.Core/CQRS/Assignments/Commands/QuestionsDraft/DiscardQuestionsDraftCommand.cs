namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionsDraft;

using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-B2 (spec §3.4 line 73) — discards a staged questions draft, clearing the
/// blob. Draft-only; idempotent when no draft is staged.
/// </summary>
public sealed record DiscardQuestionsDraftCommand(Guid AssignmentId) : ICommand;
