using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Cross-bounded-context resolver for a grade's <b>effective</b> notification policy
/// (notification-delivery-plan.md §3). The interface lives in Assignments.Core; the
/// implementation (an HTTP client to the Settings + Students APIs) lives in
/// Assignments.Api so this module stays free of HTTP.
///
/// <para>Returns the merged policy the publish handler applies to shape the broadcast
/// recipient set (drop blocked channels, order by preferred channels, cap per-sendout
/// at <c>MaxNotifications</c>). Resolution only — no delivery. When
/// <paramref name="gradeLevelId"/> is null (e.g. a group-targeted sendout), only the
/// tenant-global default applies.</para>
/// </summary>
public interface INotificationPolicyResolver
{
    Task<EffectiveNotificationPolicy> ResolveEffectiveAsync(
        Guid tenantId, Guid? gradeLevelId, CancellationToken cancellationToken = default);
}
