namespace SchoolCollab.Settings.Contracts.Events;

/// <summary>
/// Published when a tenant upserts its display override for a global coded
/// value (<c>UpsertCodedValueOverride</c>). A tenant may override Name,
/// Description, or Code (never Code AND Description simultaneously — see spec
/// §4.3); <c>null</c> fields mean "keep the global blueprint value".
/// Consumers should update their local per-tenant override row accordingly.
/// See documents/solution/adr-cross-module-calls.md.
/// </summary>
public record CodedValueOverrideUpserted(
    Guid TenantId,
    Guid GlobalCodedValueId,
    string? Name,
    string? Description,
    string? Code,
    DateTimeOffset OccurredAt);
