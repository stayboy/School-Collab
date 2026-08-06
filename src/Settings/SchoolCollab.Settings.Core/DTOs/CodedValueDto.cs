namespace SchoolCollab.Settings.Core.DTOs;

public record CodedValueDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    string? ParentCode,
    bool IsDisabled,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<CodedValueAttributeDto> Attributes,
    IReadOnlyCollection<CodedValueAttributeDefinitionDto> AttributeDefinitions,
    int ChildrenCount = 0,
    bool IsDeleted = false,
    DateTimeOffset? DeletedAt = null,
    bool IsOverridden = false,
    string? DefaultName = null,
    string? DefaultCode = null);

