using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Services;

/// <summary>
/// Writes a <see cref="FlagAuditEntry"/> into the supplied <see cref="SettingsDbContext"/>
/// so the audit row is persisted in the same transaction as the flag mutation
/// (the handler calls <see cref="SettingsDbContext.SaveChangesAsync"/> after both the
/// mutation and the audit row are tracked). Append-only: never updates or deletes.
/// </summary>
public sealed class FeatureFlagAuditor(IActorAccessor actorAccessor)
{
    public void Record(
        SettingsDbContext db,
        Guid? tenantId,
        Guid featureFlagId,
        string featureFlagKey,
        FlagChangeKind changeKind,
        bool? previousIsEnabled,
        bool? newIsEnabled,
        string? reason,
        string? previousValue = null,
        string? newValue = null)
    {
        db.FlagAuditEntries.Add(FlagAuditEntry.Create(
            tenantId: tenantId,
            featureFlagId: featureFlagId,
            featureFlagKey: featureFlagKey,
            changeKind: changeKind,
            previousIsEnabled: previousIsEnabled,
            newIsEnabled: newIsEnabled,
            reason: reason,
            actorId: actorAccessor.ActorId,
            actorDisplayName: actorAccessor.ActorDisplayName,
            previousValue: previousValue,
            newValue: newValue));
    }
}