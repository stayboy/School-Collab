using Microsoft.EntityFrameworkCore;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.Services;

/// <summary>
/// Maps the session's tenant onto a template row's owning scope (redesign spec 3.7). A real tenant owns
/// its rows; the default session (<see cref="Guid.Empty"/>) owns the <b>system blueprint</b>, which is
/// stored as <see langword="null"/> rather than as the sentinel - so <see cref="Guid.Empty"/> never
/// reaches a row. Also resolves the display owner ("System" or the tenant's name) from settings-db.
/// </summary>
public static class NotificationTemplateOwnership
{
    /// <summary>The owner label for a system-blueprint row.</summary>
    public const string SystemOwnerName = "System";

    /// <summary>The stored scope for <paramref name="currentTenantId"/>: null in system scope.</summary>
    public static Guid? OwnerId(Guid currentTenantId) => currentTenantId == Guid.Empty ? null : currentTenantId;

    /// <summary>Whether <paramref name="currentTenantId"/> is the system (blueprint-editing) scope.</summary>
    public static bool IsSystemScope(Guid currentTenantId) => currentTenantId == Guid.Empty;

    /// <summary>The owner label for one row.</summary>
    public static string OwnerName(Guid? ownerId, IReadOnlyDictionary<Guid, string> tenantNames) =>
        ownerId is { } id
            ? tenantNames.GetValueOrDefault(id, UnknownTenantName)
            : SystemOwnerName;

    /// <summary>Every tenant id to name, for the list read's single join.</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveOwnerNamesAsync(
        SettingsDbContext db, CancellationToken ct = default) =>
        await db.Tenants
            .AsNoTracking()
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

    /// <summary>One row's owner label.</summary>
    public static async Task<string> ResolveOwnerNameAsync(
        SettingsDbContext db, Guid? ownerId, CancellationToken ct = default)
    {
        if (ownerId is not { } id)
            return SystemOwnerName;

        var name = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);

        return name ?? UnknownTenantName;
    }

    /// <summary>Fallback when a stored owner no longer has a tenant row.</summary>
    public const string UnknownTenantName = "Unknown tenant";

    /// <summary>The wire projection of one row plus its resolved owner label.</summary>
    public static TenantNotificationTemplateDto ToDto(TenantNotificationTemplate template, string ownerName) =>
        new(template.Id,
            template.Kind,
            template.Channel,
            template.Title,
            template.Description,
            template.Code,
            template.Subject,
            template.Body,
            template.Constraints
                .Select(c => new TemplateConstraintDto(c.Id, c.Dimension, c.Value))
                .ToList(),
            template.CreatedAt,
            template.UpdatedAt,
            template.TenantId,
            ownerName);
}