namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionGeneration;

using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

/// <summary>
/// R3 (D4/P1-2) — records one AI question generation against an assignment: the requested shape
/// plus the provider/model the <b>AI host</b> resolved. Returns the new header's id, which the
/// caller stamps onto every question the generation produced.
/// <para>Written at draft-stage / generate time by the host that owns the assignment (never by the
/// AI host, which stays stateless). One row per generation.</para>
/// </summary>
public sealed record RecordQuestionGenerationCommand(
    Guid AssignmentId,
    int QuestionCount,
    IReadOnlyList<QuestionTypeDto>? Types,
    int? DifficultyEasyCount,
    int? DifficultyMediumCount,
    int? DifficultyHardCount,
    string? Provider,
    string? Model) : ICommand;
