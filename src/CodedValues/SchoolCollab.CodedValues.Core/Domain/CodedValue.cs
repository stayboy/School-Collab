using SchoolCollab.CodedValues.Core.Domain.Events;

namespace SchoolCollab.CodedValues.Core.Domain;

public sealed class CodedValue
{
    private readonly List<CodedValueAttribute> _attributes = [];
    private readonly List<CodedValueAttributeDefinition> _attributeDefinitions = [];
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

    /// <summary>
    /// Defines the attribute slots that children of this coded-value are expected to populate.
    /// Each definition carries data-type and source-code for UI rendering and validation.
    /// </summary>
    public IReadOnlyCollection<CodedValueAttributeDefinition> AttributeDefinitions =>
        _attributeDefinitions.AsReadOnly();

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

    public void SetAttribute(string key, string value)
    {
        var normalizedKey = key.Trim();
        var existing = _attributes.SingleOrDefault(a => a.Key == normalizedKey);
        if (existing is not null)
        {
            _attributes.Remove(existing);
        }

        _attributes.Add(new CodedValueAttribute(Id, normalizedKey, value.Trim()));
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

    /// <summary>
    /// Upserts an attribute definition describing what value children of this coded-value
    /// should supply for the given key.
    /// </summary>
    public void SetAttributeDefinition(
        string key,
        AttributeDataType dataType = AttributeDataType.Text,
        string? sourceCode = null,
        bool isRequired = false,
        bool allowMultiple = false,
        string? displayName = null)
    {
        var normalizedKey = key.Trim();
        var existing = _attributeDefinitions.SingleOrDefault(d => d.Key == normalizedKey);
        if (existing is not null)
        {
            _attributeDefinitions.Remove(existing);
        }

        _attributeDefinitions.Add(new CodedValueAttributeDefinition(
            Id, normalizedKey, dataType, sourceCode?.Trim(), isRequired, allowMultiple, displayName?.Trim()));
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Removes an attribute definition by key. Does not cascade to child attributes.</summary>
    public void RemoveAttributeDefinition(string key)
    {
        var existing = _attributeDefinitions.SingleOrDefault(d => d.Key == key.Trim());
        if (existing is null)
        {
            return;
        }

        _attributeDefinitions.Remove(existing);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}

