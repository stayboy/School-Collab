namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>D16/QR-2 (spec <c>question-response-types</c> §5.1, owner 2026-10-09): the media kinds a
/// student may answer a question with. Every question carries <b>at least one</b>; "a mix" is a set
/// of these. There is no Text member on purpose — typed answers remain
/// <see cref="QuestionType.ShortAnswer"/>. Mirrors <c>QuestionResponseKindDto</c> by value.</summary>
public enum ResponseKind
{
    Video = 0,
    Audio = 1,
    Document = 2,
    Image = 3
}
