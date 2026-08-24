using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues;

/// <summary>
/// Single source of truth for the enriched coded-value integration-event
/// payloads (adr-cross-module-calls.md, Phase 0 "complete the event contract").
/// Every mutation of projection-relevant state must emit the FULL current state
/// so downstream read models (e.g. Students local coded values) stay consistent
/// without calling back to settings-api.
/// </summary>
internal static class CodedValueEventMapper
{
    /// <summary>
    /// Resolves the parent's Code for event payloads (null for root coded
    /// values). One indexed read on the write path — acceptable per the ADR,
    /// since it removes a consumer-side settings-api hop entirely.
    /// </summary>
    public static async Task<string?> ResolveParentCodeAsync(
        ICodedValueRepository repository,
        Guid? parentId,
        CancellationToken cancellationToken)
        => parentId is { } id
            ? (await repository.GetAsync(id, cancellationToken))?.Code
            : null;

    /// <summary>Full-state created event (global blueprint, tenant-owned, or provisional).</summary>
    public static CodedValueCreated ToCreatedEvent(this CodedValue cv, string? parentCode) => new(
        cv.Id,
        cv.Code,
        cv.Name,
        cv.Description,
        cv.ParentId,
        cv.DisplayOrder,
        cv.CreatedAt,
        parentCode,
        cv.IsDisabled,
        cv.Attributes.Select(a => new CodedValueAttributeEvent(a.Key, a.Value)).ToList(),
        cv.TenantId);

    /// <summary>
    /// Full-state updated event. Also used after attribute set/remove, recovery,
    /// and provisional approval — consumers treat any CodedValueUpdated as
    /// "upsert this row as live" (IsDeleted implicitly cleared).
    /// </summary>
    public static CodedValueUpdated ToUpdatedEvent(this CodedValue cv, string? parentCode) => new(
        cv.Id,
        cv.Code,
        cv.Name,
        cv.Description,
        cv.UpdatedAt,
        cv.ParentId,
        parentCode,
        cv.DisplayOrder,
        cv.IsDisabled,
        cv.Attributes.Select(a => new CodedValueAttributeEvent(a.Key, a.Value)).ToList(),
        cv.TenantId);
}
