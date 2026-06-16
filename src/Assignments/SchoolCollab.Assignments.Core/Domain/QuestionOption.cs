namespace SchoolCollab.Assignments.Core.Domain;

public sealed class QuestionOption
{
    private QuestionOption() { }

    internal QuestionOption(Guid questionId, string optionText, bool isCorrect)
    {
        Id = Guid.NewGuid();
        QuestionId = questionId;
        OptionText = optionText;
        IsCorrect = isCorrect;
    }

    public Guid Id { get; private set; }
    public Guid QuestionId { get; private set; }
    public string OptionText { get; private set; } = default!;
    public bool IsCorrect { get; private set; }
}