using SchoolCollab.CodedValues.Core.Domain.Events;

namespace SchoolCollab.CodedValues.Core.Domain;

public sealed class CodedValue
{
    private readonly List<CodedValueAttribute> _attributes = [];
    private readonly List<IDomainEvent> _domainEvents = [];

    private CodedValue() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public Guid? ParentId { get; private set; }
    public bool IsDisabled { get; private set; }
    public int DisplayOrder { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<CodedValueAttribute> Attributes => _attributes.AsReadOnly();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static CodedValue Create(
        string code,
        string name,
        string? description,
        Guid? parentId,
        int displayOrder)
    {
        var now = DateTimeOffset.UtcNow;
        var cv = new CodedValue
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name,
            Description = description,
            ParentId = parentId,
            IsDisabled = false,
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        cv._domainEvents.Add(new CodedValueCreatedEvent(cv.Id, cv.Code, cv.Name, cv.ParentId));
        return cv;
    }

    public void Update(string name, string? description, int displayOrder)
    {
        Name = name;
        Description = description;
        DisplayOrder = displayOrder;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new CodedValueUpdatedEvent(Id, Code, Name));
    }

    public void Disable()
    {
        if (IsDisabled)
        {
            return;
        }

        IsDisabled = true;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new CodedValueDisabledEvent(Id, Code));
    }

    public void Enable()
    {
        if (!IsDisabled)
        {
            return;
        }

        IsDisabled = false;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new CodedValueEnabledEvent(Id, Code));
    }

    public void SetAttribute(
        string key,
        string value,
        AttributeDataType dataType = AttributeDataType.Text,
        string? sourceCode = null)
    {
        var normalizedKey = key.Trim();
        var existing = _attributes.SingleOrDefault(a => a.Key == normalizedKey);
        if (existing is not null)
        {
            _attributes.Remove(existing);
        }

        _attributes.Add(new CodedValueAttribute(Id, normalizedKey, value.Trim(), dataType, sourceCode?.Trim()));
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RemoveAttribute(string key)
    {
        var existing = _attributes.SingleOrDefault(a => a.Key == key);
        if (existing is null)
        {
            return;
        }

        _attributes.Remove(existing);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
