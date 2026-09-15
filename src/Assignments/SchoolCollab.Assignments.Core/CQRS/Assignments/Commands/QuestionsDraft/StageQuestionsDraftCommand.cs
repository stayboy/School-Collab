namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionsDraft;

using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-B2 (spec §3.4 line 73) — stages an AI-generated questions draft onto a
/// Draft assignment's <c>QuestionsDraftJson</c> blob. The inbound questions are
/// validated by the handler before staging so the blob is always parseable;
/// staging is Draft-only (the domain guard throws the typed
/// <see cref="InvalidQuestionsDraftException"/>).
/// </summary>
public sealed record StageQuestionsDraftCommand(
    Guid AssignmentId,
    IReadOnlyList<NewQuestionDto> Questions) : ICommand;
