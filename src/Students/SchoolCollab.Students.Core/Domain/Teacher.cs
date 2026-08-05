using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// A teacher (spec §4.12). Referenced by Assignments.Core via <c>Guid</c>.
/// Keeps a single staff email/phone (not migrated to the <see cref="Contact"/>
/// table — teachers are not notification recipients in v1). Linked to the
/// existing staff auth via <see cref="StaffUserId"/>.
/// </summary>
public sealed class Teacher : ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion
{
    private readonly List<TeacherTopic> _topics = [];
    private readonly List<TeacherGradeLevel> _gradeLevels = [];

    private Teacher() { }

    public Guid Id { get; private set; }
    public Guid? TitleCodedValueId { get; private set; }
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string? DisplayName { get; private set; }
    public string Email { get; private set; } = default!;
    public string? ContactPhone { get; private set; }
    public Guid? StaffUserId { get; private set; }
    /// <summary>Auto-generated staff number (e.g. STFA01) — spec §3.6.</summary>
    public string? StaffNumber { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<TeacherTopic> Topics => _topics.AsReadOnly();
    public IReadOnlyList<TeacherGradeLevel> GradeLevels => _gradeLevels.AsReadOnly();

    public static Teacher Create(
        Guid? titleCodedValueId, string firstName, string lastName, string? displayName, string email, string? contactPhone,
        string? staffNumber = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Teacher
        {
            Id = Guid.NewGuid(),
            TitleCodedValueId = titleCodedValueId,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            DisplayName = displayName?.Trim(),
            Email = email.Trim(),
            ContactPhone = contactPhone?.Trim(),
            StaffNumber = staffNumber?.Trim(),
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string firstName, string lastName, string? displayName, string email, string? contactPhone)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        DisplayName = displayName?.Trim();
        Email = email.Trim();
        ContactPhone = contactPhone?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void LinkTopic(Guid topicId) => _topics.Add(TeacherTopic.Create(Id, topicId));
    public void UnlinkTopic(Guid topicId) => _topics.RemoveAll(s => s.TopicId == topicId);
    public void LinkGradeLevel(Guid gradeLevelId, Guid? teacherRoleCodedValueId = null) => _gradeLevels.Add(TeacherGradeLevel.Create(Id, gradeLevelId, teacherRoleCodedValueId));
    public void UnlinkGradeLevel(Guid gradeLevelId) => _gradeLevels.RemoveAll(g => g.GradeLevelId == gradeLevelId);

    public void SoftDelete()
    {
        if (IsDeleted) return;
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Recover()
    {
        if (!IsDeleted) return;
        IsDeleted = false;
        DeletedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
