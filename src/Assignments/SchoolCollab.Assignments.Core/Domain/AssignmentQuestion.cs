namespace SchoolCollab.Assignments.Core.Domain;

public sealed class AssignmentQuestion
{
    private readonly List<QuestionOption> _options = [];

    private AssignmentQuestion() { }

    internal AssignmentQuestion(Guid assignmentId, string questionText, QuestionType questionType, int displayOrder)
    {
        Id = Guid.NewGuid();
        AssignmentId = assignmentId;
        QuestionText = questionText;
        QuestionType = questionType;
        DisplayOrder = displayOrder;
    }

    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public string QuestionText { get; private set; } = default!;
    public QuestionType QuestionType { get; private set; }
    public int DisplayOrder { get; private set; }
    public Guid? CorrectOptionId { get; private set; }

    public IReadOnlyList<QuestionOption> Options => _options.AsReadOnly();

    public QuestionOption AddOption(string optionText, bool isCorrect = false)
    {
        var option = new QuestionOption(Id, optionText, isCorrect);
        _options.Add(option);
        if (isCorrect)
            CorrectOptionId = option.Id;
        return option;
    }

    public void RemoveOption(Guid optionId)
    {
        var option = _options.SingleOrDefault(o => o.Id == optionId);
        if (option is not null)
        {
            if (CorrectOptionId == optionId)
                CorrectOptionId = null;
            _options.Remove(option);
        }
    }
}