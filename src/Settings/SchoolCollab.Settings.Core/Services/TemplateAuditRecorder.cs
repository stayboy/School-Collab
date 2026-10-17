using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Services;

/// <summary>
/// Writes a <see cref="TemplateAuditEntry"/> into the supplied <see cref="SettingsDbContext"/>
/// so the audit row is persisted in the same transaction as the template mutation
/// (the handler calls <see cref="SettingsDbContext.SaveChangesAsync"/> after both the
/// mutation and the audit row are tracked). Append-only: never updates or deletes.
/// </summary>
public sealed class TemplateAuditRecorder(IActorAccessor actorAccessor)
{
    public void Record(
        SettingsDbContext db,
        Guid? tenantId,
        Guid templateId,
        string templateKey,
        TemplateChangeKind changeKind,
        string? previousSubject,
        string? newSubject,
        string? previousBody,
        string? newBody,
        string? previousConstraints,
        string? newConstraints,
        string? reason)
    {
        db.TemplateAuditEntries.Add(TemplateAuditEntry.Create(
            tenantId: tenantId,
            templateId: templateId,
            templateKey: templateKey,
            changeKind: changeKind,
            previousSubject: previousSubject,
            newSubject: newSubject,
            previousBody: previousBody,
            newBody: newBody,
            previousConstraints: previousConstraints,
            newConstraints: newConstraints,
            reason: reason,
            actorId: actorAccessor.ActorId,
            actorDisplayName: actorAccessor.ActorDisplayName));
    }
}