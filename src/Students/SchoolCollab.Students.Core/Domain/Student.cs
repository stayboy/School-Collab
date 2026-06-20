using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

public sealed class Student : ITenantEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private Student() { }

    public Guid Id { get; private set; }
    public string StudentNumber { get; private set; } = default!;
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public DateOnly? DateOfBirth { get; private set; }
    public Guid? GenderCodedValueId { get; private set; }
    public string ContactEmail { get; private set; } = default!;
    public string? ContactPhone { get; private set; }

    // Multi-tenancy: each student belongs to a tenant (e.g., school)
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public bool IsDeleted { get; private set; }
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
        string contactEmail,
        string? contactPhone)
    {
        var now = DateTimeOffset.UtcNow;
        var student = new Student
        {
            Id = Guid.NewGuid(),
            StudentNumber = studentNumber.Trim(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            DateOfBirth = dateOfBirth,
            GenderCodedValueId = genderCodedValueId,
            ContactEmail = contactEmail.Trim(),
            ContactPhone = contactPhone?.Trim(),
            // TenantId will be set by the command handler via ITenantEntity.WithTenant()
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        student._domainEvents.Add(new StudentCreatedEvent(student.Id, student.StudentNumber));
        return student;
    }

    public void Update(
        string firstName,
        string lastName,
        DateOnly? dateOfBirth,
        Guid? genderCodedValueId,
        string contactEmail,
        string? contactPhone)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        DateOfBirth = dateOfBirth;
        GenderCodedValueId = genderCodedValueId;
        ContactEmail = contactEmail.Trim();
        ContactPhone = contactPhone?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new StudentUpdatedEvent(Id, StudentNumber));
    }

    public void Delete()
    {
        if (IsDeleted) return;
        IsDeleted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new StudentDeletedEvent(Id, StudentNumber));
    }

    public void Recover()
    {
        if (!IsDeleted) return;
        IsDeleted = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}