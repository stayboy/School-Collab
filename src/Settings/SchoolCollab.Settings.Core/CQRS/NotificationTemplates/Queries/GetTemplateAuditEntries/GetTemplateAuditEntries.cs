using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTemplateAuditEntries;

/// <summary>
/// Returns the audit entries for a template (current tenant), most recent first.
/// </summary>
public sealed record GetTemplateAuditEntries(
    NotificationTemplateKind Kind,
    NotificationChannel Channel,
    Guid? TemplateId = null) : IQuery<IReadOnlyList<TemplateAuditEntryDto>>;

/// <summary>Wire shape for a template audit entry (spec §3.9: change kind, before/after
/// subject/body, and the constraint summary of each side).</summary>
public sealed record TemplateAuditEntryDto(
    Guid Id,
    Guid TemplateId,
    TemplateChangeKind ChangeKind,
    string? PreviousSubject,
    string? NewSubject,
    string? PreviousBody,
    string? NewBody,
    string? PreviousConstraints,
    string? NewConstraints,
    string? Reason,
    string ActorId,
    string ActorDisplayName,
    DateTimeOffset OccurredAt);