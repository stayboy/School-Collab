namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionsDraft;

using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-B2 (spec §3.4 line 73) — confirms a staged questions draft, REPLACING all
/// existing questions with the drafted set. Confirmation is Draft-only; a missing
/// or corrupt blob throws <see cref="InvalidQuestionsDraftException"/> (defensive
/// — staging validates first).
/// </summary>
public sealed record ConfirmQuestionsDraftCommand(Guid AssignmentId) : ICommand;
