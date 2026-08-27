using SchoolCollab.Assignments.Core.Domain.Events;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

public sealed class Assignment : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
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
    // Operational references into the Students bounded context (global GradeLevel/
    // Topic entities). These replace the former coded-value ids so the assignment
    // reports against the real operational entities; display names are still
    // resolved client-side from tenant-resolved coded values (spec §5.7).
    public Guid TopicId { get; private set; }
    public Guid? GradeLevelId { get; private set; }
    /// <summary>Auto-generated assignment code (e.g. ASGA01) — spec §3.6.</summary>
    public string? AssignmentNumber { get; private set; }

    // Multi-tenancy: all assignments belong to a tenant (e.g., school or organization)
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public DateTimeOffset? DueDate { get; private set; }
    public decimal? MaxScore { get; private set; }
    public AssignmentStatus Status { get; private set; }
    public Guid CreatedByTeacherId { get; private set; }
    /// <summary>
    /// When true (default), student self-submit is blocked until a Primary
    /// guardian reviews + enables (or submits on behalf). When false, the
    /// gate is optional (spec §4.7).
    /// </summary>
    public bool MandatoryReview { get; private set; }
    /// <summary>Set when the assignment is published (spec §4.8). Null while Draft.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }
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
        Guid topicId,
        Guid? gradeLevelId,
        DateTimeOffset? dueDate,
        decimal? maxScore,
        Guid createdByTeacherId = default,
        bool mandatoryReview = true,
        string? assignmentNumber = null)
    {
        if (topicId == Guid.Empty)
            throw new ArgumentException("Topic is required.", nameof(topicId));
        if (targetAudienceType == TargetAudienceType.SelectedGrades && !gradeLevelId.HasValue)
            throw new ArgumentException("SelectedGrades assignments require a grade level.", nameof(gradeLevelId));

        var now = DateTimeOffset.UtcNow;
        var assignment = new Assignment
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            Description = description?.Trim(),
            AssignmentType = assignmentType,
            GradingFormat = gradingFormat,
            TargetAudienceType = targetAudienceType,
            TopicId = topicId,
            GradeLevelId = gradeLevelId,
            DueDate = dueDate,
            MaxScore = maxScore,
            Status = AssignmentStatus.Draft,
            CreatedByTeacherId = createdByTeacherId,
            // Mandatory review is the default (spec §4.7); callers may opt out.
            MandatoryReview = mandatoryReview,
            AssignmentNumber = assignmentNumber?.Trim(),
            // TenantId will be set by the command handler via ITenantEntity.WithTenant()
            CreatedAt = now,
            UpdatedAt = now
        };

        assignment._domainEvents.Add(new AssignmentCreatedEvent(assignment.Id, assignment.Title));
        return assignment;
    }

    public void Update(string title, string? description, AssignmentType assignmentType,
        GradingFormat gradingFormat, TargetAudienceType targetAudienceType,
        Guid topicId, Guid? gradeLevelId, DateTimeOffset? dueDate, decimal? maxScore,
        bool mandatoryReview)
    {
        if (Status != AssignmentStatus.Draft)
            throw new InvalidOperationException("Only draft assignments can be updated.");
        if (topicId == Guid.Empty)
            throw new ArgumentException("Topic is required.", nameof(topicId));
        if (targetAudienceType == TargetAudienceType.SelectedGrades && !gradeLevelId.HasValue)
            throw new ArgumentException("SelectedGrades assignments require a grade level.", nameof(gradeLevelId));

        Title = title.Trim();
        Description = description?.Trim();
        AssignmentType = assignmentType;
        GradingFormat = gradingFormat;
        TargetAudienceType = targetAudienceType;
        TopicId = topicId;
        GradeLevelId = gradeLevelId;
        DueDate = dueDate;
        MaxScore = maxScore;
        MandatoryReview = mandatoryReview;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentUpdatedEvent(Id, Title));
    }

    public void Publish()
    {
        if (Status == AssignmentStatus.Published)
            return;

        Status = AssignmentStatus.Published;
        PublishedAt = DateTimeOffset.UtcNow;
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