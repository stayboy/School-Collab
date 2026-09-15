namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.QuestionsDraft;

using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-B2 (spec §3.4 line 73) — reads a staged questions draft. Null when no
/// draft is staged; a corrupt blob throws the typed
/// <see cref="InvalidQuestionsDraftException"/> (defensive).
/// </summary>
public sealed record GetQuestionsDraftQuery(Guid AssignmentId) : IQuery<IReadOnlyList<NewQuestionDto>?>;
