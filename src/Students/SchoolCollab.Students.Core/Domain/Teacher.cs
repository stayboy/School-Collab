using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// A teacher (spec §4.12). Referenced by Assignments.Core via <c>Guid</c>.
/// Contact channels (Email/SMS/WhatsApp) live on the shared <see cref="Contact"/>
/// table keyed by <see cref="ContactOwnerType.Teacher"/>, so teachers can be
/// notification recipients like students and guardians. Linked to the existing
/// staff auth via <see cref="StaffUserId"/>.
/// </summary>
public sealed class Teacher : PersonDemographic, ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion
{
    private readonly List<TeacherTopic> _topics = [];
    private readonly List<TeacherGradeLevel> _gradeLevels = [];
    private readonly List<TeacherQualification> _qualifications = [];
    private readonly List<TeacherActivityAssignment> _activityAssignments = [];

    private Teacher() { }

    public Guid Id { get; private set; }
    public string? DisplayName { get; private set; }
    public Guid? StaffUserId { get; private set; }
    /// <summary>Auto-generated staff number (e.g. STFA01) — spec §3.6.</summary>
    public string? StaffNumber { get; private set; }
    /// <summary>Highest level of education (coded value, <c>EDUCLEVEL</c> parent).</summary>
    public Guid? LevelOfEducationCodedValueId { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<TeacherTopic> Topics => _topics.AsReadOnly();
    public IReadOnlyList<TeacherGradeLevel> GradeLevels => _gradeLevels.AsReadOnly();
    public IReadOnlyList<TeacherQualification> Qualifications => _qualifications.AsReadOnly();
    public IReadOnlyList<TeacherActivityAssignment> ActivityAssignments => _activityAssignments.AsReadOnly();

    public static Teacher Create(
        Guid? titleCodedValueId, string firstName, string lastName, string? displayName,
        string? staffNumber = null, Guid? genderCodedValueId = null, DateOnly? dateOfBirth = null,
        Guid? levelOfEducationCodedValueId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var teacher = new Teacher
        {
            Id = Guid.NewGuid(),
            StaffNumber = staffNumber?.Trim(),
            LevelOfEducationCodedValueId = levelOfEducationCodedValueId,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        teacher.SetDemographics(titleCodedValueId, firstName, lastName, dateOfBirth, genderCodedValueId);
        teacher.DisplayName = displayName?.Trim();
        return teacher;
    }

    public void Update(string firstName, string lastName, string? displayName,
        Guid? genderCodedValueId = null, DateOnly? dateOfBirth = null, Guid? levelOfEducationCodedValueId = null)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        DisplayName = displayName?.Trim();
        GenderCodedValueId = genderCodedValueId;
        DateOfBirth = dateOfBirth;
        LevelOfEducationCodedValueId = levelOfEducationCodedValueId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void LinkQualification(Guid codedValueId)
    {
        if (_qualifications.All(q => q.CodedValueId != codedValueId))
            _qualifications.Add(TeacherQualification.Create(Id, codedValueId));
    }

    public void UnlinkQualification(Guid codedValueId)
        => _qualifications.RemoveAll(q => q.CodedValueId == codedValueId);

    public void LinkTopic(Guid topicId) => _topics.Add(TeacherTopic.Create(Id, topicId));
    public void UnlinkTopic(Guid topicId) => _topics.RemoveAll(s => s.TopicId == topicId);
    public void LinkGradeLevel(Guid gradeLevelId, Guid? topicId = null, Guid? teacherRoleCodedValueId = null)
    {
        if (_gradeLevels.Any(g => g.GradeLevelId == gradeLevelId && g.TopicId == topicId)) return;
        _gradeLevels.Add(TeacherGradeLevel.Create(Id, gradeLevelId, topicId, teacherRoleCodedValueId));
    }
    public void UnlinkGradeLevel(Guid gradeLevelId) => _gradeLevels.RemoveAll(g => g.GradeLevelId == gradeLevelId);
    public void UnlinkGradeLevelRow(Guid rowId) => _gradeLevels.RemoveAll(g => g.Id == rowId);
    public void LinkActivityAssignment(Guid activityGroupId, Guid? roleCodedValueId = null, IEnumerable<Guid>? gradeLevelIds = null)
        => _activityAssignments.Add(TeacherActivityAssignment.Create(Id, activityGroupId, roleCodedValueId, gradeLevelIds));
    public void UnlinkActivityAssignment(Guid id) => _activityAssignments.RemoveAll(a => a.Id == id);

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
