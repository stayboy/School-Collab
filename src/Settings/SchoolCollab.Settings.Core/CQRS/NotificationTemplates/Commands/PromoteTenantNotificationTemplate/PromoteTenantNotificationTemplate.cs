using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.PromoteTenantNotificationTemplate;

/// <summary>
/// Publishes a template portal-wide (redesign spec 3.8): copies a tenant's row into the system
/// blueprint (<c>TenantId = NULL</c>) so every tenant sees it unless it owns an override. The source is
/// never modified and an identical system-side constraint set is a conflict. Requires system scope -
/// this is the one deliberate cross-tenant read in the feature, gated by scope rather than by a claim.
/// </summary>
public sealed record PromoteTenantNotificationTemplate(Guid TemplateId, string? Reason) : ICommand;