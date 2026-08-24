namespace SchoolCollab.Settings.Contracts.Events;

/// <summary>
/// Published when a coded value is created (global blueprint, provisional
/// tenant-owned, or approved). Enriched with ParentCode/IsDisabled/Attributes so
/// downstream projections can build a complete local read model without calling
/// back to settings-api — see documents/solution/adr-cross-module-calls.md.
/// </summary>
/// <remarks>New parameters have defaults so messages enqueued by a pre-enrichment
/// producer still deserialize after rollout.</remarks>
public record CodedValueCreated(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    string? ParentCode = null,
    bool IsDisabled = false,
    IReadOnlyList<CodedValueAttributeEvent>? Attributes = null,
    Guid? TenantId = null);
