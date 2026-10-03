namespace SchoolCollab.Assignments.Core.Domain;

public sealed class AssignmentQuestion
{
    private readonly List<QuestionOption> _options = [];

    private AssignmentQuestion() { }

    internal AssignmentQuestion(Guid assignmentId, string questionText, QuestionType questionType, int displayOrder, string? modelAnswer = null, Guid? generationId = null)
    {
        Id = Guid.NewGuid();
        AssignmentId = assignmentId;
        QuestionText = questionText;
        QuestionType = questionType;
        DisplayOrder = displayOrder;
        ModelAnswer = modelAnswer;
        GenerationId = generationId;
    }

    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public string QuestionText { get; private set; } = default!;
    public QuestionType QuestionType { get; private set; }
    public int DisplayOrder { get; private set; }
    public Guid? CorrectOptionId { get; private set; }
    /// <summary>Optional reference answer kept on the question for teacher reference
    /// (AI spec decision 10 / §5). Free-text for <see cref="QuestionType.ShortAnswer"/>
    /// (teacher grades manually); null for MC/TF.</summary>
    public string? ModelAnswer { get; private set; }

    /// <summary>R3 / D4 (P1-2): the <see cref="AssignmentQuestionGeneration"/> header this question
    /// was produced by, or null for a hand-written question. Nullable on purpose — the ratified
    /// requirement is "every *generation* is saved", not "every question came from one".
    /// <para>These rows are re-minted on every save (<c>RemoveQuestion</c> → <c>AddQuestion</c>), so
    /// this value survives only because it rides <c>NewQuestionDto</c> through the create/update and
    /// the questions-draft confirm path. Dropping it there silently strips provenance.</para></summary>
    public Guid? GenerationId { get; private set; }

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