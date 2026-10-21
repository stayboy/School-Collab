using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.AdoptTenantNotificationTemplate;

/// <summary>
/// Copies a <b>system-blueprint</b> template into the caller's tenant (redesign spec 3.8): the copy
/// carries the subject, body and every constraint row, the source is never modified, and an identical
/// constraint set already present in the tenant is a conflict rather than a duplicate. Requires a real
/// tenant session - the system session has nothing to adopt into.
/// </summary>
public sealed record AdoptTenantNotificationTemplate(Guid TemplateId, string? Reason) : ICommand;