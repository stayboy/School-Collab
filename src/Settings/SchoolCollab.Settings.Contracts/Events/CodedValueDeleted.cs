namespace SchoolCollab.Settings.Contracts.Events;

/// <summary>
/// Published when a coded value is soft-deleted (<c>DeleteCodedValue</c>).
/// Consumers should remove their local projection row (or mark it unavailable) —
/// a deleted coded value must not validate any downstream write (e.g. enroll
/// stream validation must treat it as not-found).
/// See documents/solution/adr-cross-module-calls.md.
/// </summary>
public record CodedValueDeleted(
    Guid Id,
    string Code,
    DateTimeOffset DeletedAt);
