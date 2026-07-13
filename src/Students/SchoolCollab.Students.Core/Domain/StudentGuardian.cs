using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Link between a student and a guardian (spec §4.3). Role only — no ordering.
/// Multiple Primary allowed. Retained (not cascaded) when the guardian is
/// soft-deleted. Not itself soft-deletable.
/// </summary>
public sealed class StudentGuardian : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private StudentGuardian() { }

    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid GuardianId { get; private set; }
    public Guid? RelationshipCodedValueId { get; private set; }
    public GuardianRole Role { get; private set; }
    public bool IsEmergencyContact { get; private set; }
    public Guid? CreatedByGuardianId { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static StudentGuardian Create(
        Guid studentId,
        Guid guardianId,
        GuardianRole role,
        Guid? relationshipCodedValueId,
        bool isEmergencyContact,
        Guid? createdByGuardianId)
    {
        var now = DateTimeOffset.UtcNow;
        return new StudentGuardian
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            GuardianId = guardianId,
            Role = role,
            RelationshipCodedValueId = relationshipCodedValueId,
            IsEmergencyContact = isEmergencyContact,
            CreatedByGuardianId = createdByGuardianId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdateRole(GuardianRole role, Guid? relationshipCodedValueId)
    {
        Role = role;
        RelationshipCodedValueId = relationshipCodedValueId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Full link update used by <c>UpdateGuardianLink</c>.</summary>
    public void Update(GuardianRole role, Guid? relationshipCodedValueId, bool isEmergencyContact)
    {
        Role = role;
        RelationshipCodedValueId = relationshipCodedValueId;
        IsEmergencyContact = isEmergencyContact;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
