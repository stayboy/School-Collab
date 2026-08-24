namespace SchoolCollab.Settings.Contracts.Events;

/// <summary>
/// Published when a coded value's global fields change. Enriched beyond the
/// original (Id/Code/Name/Description/UpdatedAt) so downstream projections can
/// maintain a complete local read model without calling back to settings-api —
/// see documents/solution/adr-cross-module-calls.md.
/// </summary>
/// <remarks>New parameters have defaults so messages enqueued by a pre-enrichment
/// producer still deserialize after rollout.</remarks>
public record CodedValueUpdated(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    DateTimeOffset UpdatedAt,
    Guid? ParentId = null,
    string? ParentCode = null,
    int DisplayOrder = 0,
    bool IsDisabled = false,
    IReadOnlyList<CodedValueAttributeEvent>? Attributes = null,
    Guid? TenantId = null);
