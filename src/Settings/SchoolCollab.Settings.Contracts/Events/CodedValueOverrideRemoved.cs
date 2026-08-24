namespace SchoolCollab.Settings.Contracts.Events;

/// <summary>
/// Published when a tenant removes its override for a global coded value
/// (<c>RemoveCodedValueOverride</c>, idempotent — only published when an
/// override row actually existed). Consumers should drop their local override
/// row and fall back to the global blueprint values.
/// See documents/solution/adr-cross-module-calls.md.
/// </summary>
public record CodedValueOverrideRemoved(
    Guid TenantId,
    Guid GlobalCodedValueId,
    DateTimeOffset OccurredAt);
