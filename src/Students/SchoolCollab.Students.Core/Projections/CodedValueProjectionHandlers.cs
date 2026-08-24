using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Projections;

/// <summary>
/// Maintains the local coded-value read model from Settings integration events
/// (adr-cross-module-calls.md Phase 1). Implements the consumer rules recorded
/// in the ADR's Phase 0 section. All handlers invalidate the "coded-values"
/// cache tag so <see cref="LocalCodedValueRepository"/> never serves stale
/// values after a change.
///
/// <para>Handlers use <c>IDbContextFactory</c>: they run in the worker's
/// background scope, which may outlive any request scope.</para>
/// </summary>
public abstract class CodedValueProjectionHandlerBase(
    IDbContextFactory<StudentsDbContext> dbFactory,
    HybridCache cache)
{
    protected async Task<StudentsDbContext> CreateDbAsync(CancellationToken ct)
        => await dbFactory.CreateDbContextAsync(ct);

    protected async Task InvalidateCacheAsync(CancellationToken ct)
        => await cache.RemoveByTagAsync("coded-values", ct);

    /// <summary>Upsert by (TenantId, Id) with full state; IsDeleted cleared.</summary>
    protected static async Task UpsertAsync(StudentsDbContext db, LocalCodedValue incoming, CancellationToken ct)
    {
        var existing = await db.LocalCodedValues
            .SingleOrDefaultAsync(x => x.TenantId == incoming.TenantId && x.Id == incoming.Id, ct);

        if (existing is null)
        {
            db.LocalCodedValues.Add(incoming);
        }
        else
        {
            existing.Code = incoming.Code;
            existing.Name = incoming.Name;
            existing.Description = incoming.Description;
            existing.ParentId = incoming.ParentId;
            existing.ParentCode = incoming.ParentCode;
            existing.IsDisabled = incoming.IsDisabled;
            existing.IsDeleted = false; // Updated ⇒ live
            existing.DisplayOrder = incoming.DisplayOrder;
            existing.Attributes = incoming.Attributes;
            existing.CreatedAt = incoming.CreatedAt;
            existing.UpdatedAt = incoming.UpdatedAt;
        }

        await db.SaveChangesAsync(ct);
    }

    private protected static List<LocalCodedValueAttribute> MapAttributes(IReadOnlyList<CodedValueAttributeEvent>? attributes)
        => attributes?.Select(a => new LocalCodedValueAttribute(a.Key, a.Value)).ToList() ?? [];
}

/// <summary>Created/Updated: upsert the row under the event's tenancy.</summary>
public sealed class CodedValueCreatedProjectionHandler(
    IDbContextFactory<StudentsDbContext> dbFactory,
    HybridCache cache)
    : CodedValueProjectionHandlerBase(dbFactory, cache), IIntegrationEventHandler<CodedValueCreated>
{
    public async Task HandleAsync(CodedValueCreated @event, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateDbAsync(cancellationToken);
        await UpsertAsync(db, new LocalCodedValue
        {
            Id = @event.Id,
            TenantId = @event.TenantId,
            Code = @event.Code,
            Name = @event.Name,
            Description = @event.Description,
            ParentId = @event.ParentId,
            ParentCode = @event.ParentCode,
            IsDisabled = @event.IsDisabled,
            DisplayOrder = @event.DisplayOrder,
            Attributes = MapAttributes(@event.Attributes),
            CreatedAt = @event.CreatedAt,
            UpdatedAt = @event.CreatedAt,
        }, cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
    }
}

/// <summary>
/// Updated: upsert as live. On provisional approval the event arrives with
/// <c>TenantId = null</c>; drop any stale tenant-owned row for the same Id so
/// the value is purely global afterwards.
/// </summary>
public sealed class CodedValueUpdatedProjectionHandler(
    IDbContextFactory<StudentsDbContext> dbFactory,
    HybridCache cache)
    : CodedValueProjectionHandlerBase(dbFactory, cache), IIntegrationEventHandler<CodedValueUpdated>
{
    public async Task HandleAsync(CodedValueUpdated @event, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateDbAsync(cancellationToken);

        await UpsertAsync(db, new LocalCodedValue
        {
            Id = @event.Id,
            TenantId = @event.TenantId,
            Code = @event.Code,
            Name = @event.Name,
            Description = @event.Description,
            ParentId = @event.ParentId,
            ParentCode = @event.ParentCode,
            IsDisabled = @event.IsDisabled,
            DisplayOrder = @event.DisplayOrder,
            Attributes = MapAttributes(@event.Attributes),
            CreatedAt = @event.UpdatedAt,
            UpdatedAt = @event.UpdatedAt,
        }, cancellationToken);

        if (@event.TenantId == null)
        {
            // Approval reconciles tenancy tenant→global: remove leftover rows.
            var stale = await db.LocalCodedValues
                .Where(x => x.Id == @event.Id && x.TenantId != null)
                .ToListAsync(cancellationToken);
            if (stale.Count > 0)
            {
                db.LocalCodedValues.RemoveRange(stale);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await InvalidateCacheAsync(cancellationToken);
    }
}

/// <summary>Disabled/Enabled: toggle IsDisabled on every row for the Id.</summary>
public sealed class CodedValueDisabledProjectionHandler(
    IDbContextFactory<StudentsDbContext> dbFactory,
    HybridCache cache)
    : CodedValueProjectionHandlerBase(dbFactory, cache), IIntegrationEventHandler<CodedValueDisabled>
{
    public async Task HandleAsync(CodedValueDisabled @event, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateDbAsync(cancellationToken);
        await db.LocalCodedValues
            .Where(x => x.Id == @event.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDisabled, true), cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
    }
}

public sealed class CodedValueEnabledProjectionHandler(
    IDbContextFactory<StudentsDbContext> dbFactory,
    HybridCache cache)
    : CodedValueProjectionHandlerBase(dbFactory, cache), IIntegrationEventHandler<CodedValueEnabled>
{
    public async Task HandleAsync(CodedValueEnabled @event, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateDbAsync(cancellationToken);
        await db.LocalCodedValues
            .Where(x => x.Id == @event.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDisabled, false), cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
    }
}

/// <summary>Deleted: soft-deleted upstream — drop every row for the Id (incl. orphaned overlays).</summary>
public sealed class CodedValueDeletedProjectionHandler(
    IDbContextFactory<StudentsDbContext> dbFactory,
    HybridCache cache)
    : CodedValueProjectionHandlerBase(dbFactory, cache), IIntegrationEventHandler<CodedValueDeleted>
{
    public async Task HandleAsync(CodedValueDeleted @event, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateDbAsync(cancellationToken);
        var rows = await db.LocalCodedValues
            .Where(x => x.Id == @event.Id)
            .ToListAsync(cancellationToken);
        if (rows.Count > 0)
        {
            db.LocalCodedValues.RemoveRange(rows);
            await db.SaveChangesAsync(cancellationToken);
        }
        await InvalidateCacheAsync(cancellationToken);
    }
}

/// <summary>Override upserted: store overlay row exactly as carried (null fields keep global values at read time).</summary>
public sealed class CodedValueOverrideUpsertedProjectionHandler(
    IDbContextFactory<StudentsDbContext> dbFactory,
    HybridCache cache)
    : CodedValueProjectionHandlerBase(dbFactory, cache), IIntegrationEventHandler<CodedValueOverrideUpserted>
{
    public async Task HandleAsync(CodedValueOverrideUpserted @event, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateDbAsync(cancellationToken);

        var existing = await db.LocalCodedValues
            .SingleOrDefaultAsync(x => x.TenantId == @event.TenantId && x.Id == @event.GlobalCodedValueId, cancellationToken);

        if (existing is null)
        {
            // Overlay without a mirrored global row yet (create event still in
            // flight): store a placeholder global-shaped row so the overlay has
            // an anchor; the Created event will overwrite it with full state.
            db.LocalCodedValues.Add(new LocalCodedValue
            {
                Id = @event.GlobalCodedValueId,
                TenantId = @event.TenantId,
                // Null fields = "keep global at read time"; never substitute empty
                // string (Resolve's `overlay ?? source` would then win with "").
                Code = @event.Code,
                Name = @event.Name,
                Description = @event.Description,
                Attributes = [],
                CreatedAt = @event.OccurredAt,
                UpdatedAt = @event.OccurredAt,
            });
        }
        else
        {
            existing.Code = @event.Code;
            existing.Name = @event.Name;
            existing.Description = @event.Description;
            existing.UpdatedAt = @event.OccurredAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
    }
}

/// <summary>Override removed: drop the overlay row so reads fall back to global.</summary>
public sealed class CodedValueOverrideRemovedProjectionHandler(
    IDbContextFactory<StudentsDbContext> dbFactory,
    HybridCache cache)
    : CodedValueProjectionHandlerBase(dbFactory, cache), IIntegrationEventHandler<CodedValueOverrideRemoved>
{
    public async Task HandleAsync(CodedValueOverrideRemoved @event, CancellationToken cancellationToken = default)
    {
        await using var db = await CreateDbAsync(cancellationToken);
        var existing = await db.LocalCodedValues
            .SingleOrDefaultAsync(x => x.TenantId == @event.TenantId && x.Id == @event.GlobalCodedValueId, cancellationToken);
        if (existing is not null)
        {
            db.LocalCodedValues.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
        await InvalidateCacheAsync(cancellationToken);
    }
}
