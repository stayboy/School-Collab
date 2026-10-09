namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>QR-5 (spec <c>question-response-types</c> §5.2/§5.6, owner 2026-10-09): the kind of one
/// instruction block. The same member set serves a question and the assignment itself. Mirrors
/// <c>InstructionKindDto</c> by value.</summary>
public enum InstructionKind
{
    Text = 0,
    Url = 1,
    Audio = 2,
    Video = 3,
    Image = 4
}
