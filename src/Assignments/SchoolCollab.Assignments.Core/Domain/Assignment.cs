using SchoolCollab.Assignments.Core.Domain.Events;

namespace SchoolCollab.Assignments.Core.Domain;

public sealed class Assignment
{
    private readonly List<AssignmentQuestion> _questions = [];
    private readonly List<AssignmentReview> _reviews = [];
    private readonly List<AssignmentAttachment> _attachments = [];
    private readonly List<IDomainEvent> _domainEvents = [];

    private Assignment() { }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public AssignmentType AssignmentType { get; private set; }
    public GradingFormat GradingFormat { get; private set; }
    public TargetAudienceType TargetAudienceType { get; private set; }
    public Guid SubjectCodedValueId { get; private set; }
    public Guid? GradeCodedValueId { get; private set; }
    public DateTimeOffset? DueDate { get; private set; }
    public decimal? MaxScore { get; private set; }
    public AssignmentStatus Status { get; private set; }
    public Guid CreatedByTeacherId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<AssignmentQuestion> Questions => _questions.AsReadOnly();
    public IReadOnlyList<AssignmentReview> Reviews => _reviews.AsReadOnly();
    public IReadOnlyList<AssignmentAttachment> Attachments => _attachments.AsReadOnly();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static Assignment Create(
        string title,
        string? description,
        AssignmentType assignmentType,
        GradingFormat gradingFormat,
        TargetAudienceType targetAudienceType,
        Guid subjectCodedValueId,
        Guid? gradeCodedValueId,
        DateTimeOffset? dueDate,
        decimal? maxScore,
        Guid createdByTeacherId)
    {
        var now = DateTimeOffset.UtcNow;
        var assignment = new Assignment
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            Description = description?.Trim(),
            AssignmentType = assignmentType,
            GradingFormat = gradingFormat,
            TargetAudienceType = targetAudienceType,
            SubjectCodedValueId = subjectCodedValueId,
            GradeCodedValueId = gradeCodedValueId,
            DueDate = dueDate,
            MaxScore = maxScore,
            Status = AssignmentStatus.Draft,
            CreatedByTeacherId = createdByTeacherId,
            CreatedAt = now,
            UpdatedAt = now
        };

        assignment._domainEvents.Add(new AssignmentCreatedEvent(assignment.Id, assignment.Title));
        return assignment;
    }

    public void Update(string title, string? description, AssignmentType assignmentType,
        GradingFormat gradingFormat, TargetAudienceType targetAudienceType,
        Guid subjectCodedValueId, Guid? gradeCodedValueId, DateTimeOffset? dueDate, decimal? maxScore)
    {
        if (Status != AssignmentStatus.Draft)
            throw new InvalidOperationException("Only draft assignments can be updated.");

        Title = title.Trim();
        Description = description?.Trim();
        AssignmentType = assignmentType;
        GradingFormat = gradingFormat;
        TargetAudienceType = targetAudienceType;
        SubjectCodedValueId = subjectCodedValueId;
        GradeCodedValueId = gradeCodedValueId;
        DueDate = dueDate;
        MaxScore = maxScore;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentUpdatedEvent(Id, Title));
    }

    public void Publish()
    {
        if (Status == AssignmentStatus.Published)
            return;

        Status = AssignmentStatus.Published;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentPublishedEvent(Id, Title));
    }

    public void Unpublish()
    {
        if (Status != AssignmentStatus.Published)
            throw new InvalidOperationException("Only published assignments can be unpublished.");

        Status = AssignmentStatus.Draft;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentUnpublishedEvent(Id, Title));
    }

    public void Close()
    {
        if (Status == AssignmentStatus.Closed)
            return;

        Status = AssignmentStatus.Closed;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentClosedEvent(Id, Title));
    }

    public AssignmentQuestion AddQuestion(string questionText, QuestionType questionType, int displayOrder)
    {
        var question = new AssignmentQuestion(Id, questionText, questionType, displayOrder);
        _questions.Add(question);
        UpdatedAt = DateTimeOffset.UtcNow;
        return question;
    }

    public void RemoveQuestion(Guid questionId)
    {
        var question = _questions.SingleOrDefault(q => q.Id == questionId);
        if (question is not null)
        {
            _questions.Remove(question);
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public AssignmentReview AddReview(Guid teacherId, decimal? score, string? comments)
    {
        var review = new AssignmentReview(Id, teacherId, score, comments);
        _reviews.Add(review);
        UpdatedAt = DateTimeOffset.UtcNow;
        return review;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}