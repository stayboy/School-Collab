namespace SchoolCollab.Core.Notifications;

/// <summary>
/// Resolves the effective notification policy for a grade by merging the tenant's
/// global default with the grade's optional override: a non-null grade field wins,
/// otherwise the tenant value is used (which may itself be null = built-in default).
/// </summary>
public interface IEffectiveNotificationPolicyResolver
{
    EffectiveNotificationPolicy Resolve(
        NotificationPolicyFields? tenantDefault,
        NotificationPolicyFields? gradeOverride);
}
