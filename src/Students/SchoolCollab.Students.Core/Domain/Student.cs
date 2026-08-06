using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class Student : PersonDemographic, ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private Student() { }

    public Guid Id { get; private set; }
    public string StudentNumber { get; private set; } = default!;

    // Multi-tenancy: each student belongs to a tenant (e.g., school)
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static Student Create(
        string studentNumber,
        string firstName,
        string lastName,
        DateOnly? dateOfBirth,
        Guid? genderCodedValueId,
        Guid? titleCodedValueId = null)
    {
        if (string.IsNullOrWhiteSpace(studentNumber))
            throw new ArgumentException("Student number is required.", nameof(studentNumber));
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required.", nameof(lastName));
        if (dateOfBirth is null)
            throw new ArgumentException("Date of birth is required.", nameof(dateOfBirth));
        if (genderCodedValueId is null)
            throw new ArgumentException("Gender is required.", nameof(genderCodedValueId));

        var now = DateTimeOffset.UtcNow;
        var student = new Student
        {
            Id = Guid.NewGuid(),
            StudentNumber = studentNumber.Trim(),
            // TenantId will be set by the command handler via ITenantEntity.WithTenant()
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        student.SetDemographics(titleCodedValueId, firstName, lastName, dateOfBirth, genderCodedValueId);

        student._domainEvents.Add(new StudentCreatedEvent(student.Id, student.StudentNumber));
        return student;
    }

    public void Update(
        string firstName,
        string lastName,
        DateOnly? dateOfBirth,
        Guid? genderCodedValueId,
        Guid? titleCodedValueId = null)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required.", nameof(lastName));
        if (dateOfBirth is null)
            throw new ArgumentException("Date of birth is required.", nameof(dateOfBirth));
        if (genderCodedValueId is null)
            throw new ArgumentException("Gender is required.", nameof(genderCodedValueId));

        SetDemographics(titleCodedValueId, firstName, lastName, dateOfBirth, genderCodedValueId);
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new StudentUpdatedEvent(Id, StudentNumber));
    }

    public void Delete()
    {
        if (IsDeleted) return;
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new StudentDeletedEvent(Id, StudentNumber));
    }

    public void Recover()
    {
        if (!IsDeleted) return;
        IsDeleted = false;
        DeletedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}