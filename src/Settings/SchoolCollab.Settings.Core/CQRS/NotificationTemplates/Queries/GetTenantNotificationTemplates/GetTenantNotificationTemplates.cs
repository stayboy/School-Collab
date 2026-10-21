using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplates;

/// <summary>
/// Returns every template the caller's scope can see - its own rows plus the system blueprint - with
/// their constraints and owner (redesign spec 3.2/3.7). Empty means nothing is configured and the
/// built-in defaults apply everywhere.
/// <para>
/// <see cref="IncludeTenantOwned"/> is the promote picker's source (spec 3.6): honoured <b>only</b> in
/// system scope, where it reads every tenant's rows so one can be promoted to the blueprint. A tenant
/// session that passes it gets the flag ignored - never a cross-tenant read.
/// </para>
/// </summary>
public sealed record GetTenantNotificationTemplates(bool IncludeTenantOwned = false)
    : IQuery<IReadOnlyList<TenantNotificationTemplateDto>>;