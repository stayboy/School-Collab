using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Cross-bounded-context contact resolver (spec §9 G5 / Phase 6). Resolves the
/// set of subscribed contacts that should receive an assignment broadcast for a
/// given publish scope. The interface lives in Assignments.Core; the
/// implementation (an HTTP client to the Students API) lives in Assignments.Api
/// so this module stays free of HTTP.
/// </summary>
public interface IContactResolver
{
    Task<IReadOnlyList<SubscriberInfo>> ResolveSubscribersAsync(
        ResolveSubscribersRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// A single subscribed contact to notify, resolved from the Students bounded
/// context. <see cref="StudentId"/> ties the contact back to the student it
/// concerns (the ward), which drives both the per-contact recipient row and the
/// per-(assignment, student) guardian submission gate.
/// <para><see cref="ContactName"/> / <see cref="StudentName"/> are the spec §3.4 enrichment
/// names (round notification-templates-b1): they arrive on the same Students response, and
/// each degrades to <see cref="string.Empty"/> when the name is absent — a name that cannot
/// be resolved never fails the publish.</para>
/// </summary>
public sealed record SubscriberInfo(
    Guid ContactId,
    ContactOwnerType OwnerType,
    Guid OwnerId,
    Guid? StudentId,
    ContactChannel Channel,
    GuardianRole? Role,
    string ContactName = "",
    string StudentName = "");

/// <summary>
/// Request to resolve publish recipients. Provide an explicit <see cref="StudentIds"/> roster
/// (already resolved from the assignment's authored targets) and/or the assignment's distinct
/// grade-target ids (<see cref="GradeLevelIds"/>, which add each grade's teachers to the cohort).
/// The by-grade whole-roster fallback is gone (round <c>drop-primary-grade</c>): the target
/// resolver is the sole student source and publish refuses an empty resolved set before this
/// resolver is reached. When neither is supplied the result is empty.
/// <para><see cref="TopicId"/> is the assignment's subject, when it has one
/// (subject-enrollment-requirement-flag.md FR-ER-13b). It is <c>null</c> for a publish with no
/// subject, which is <b>not</b> subject-scoped: the eligibility hop is skipped entirely and the
/// recipient set is exactly what it was before the filter existed.</para>
/// </summary>
public sealed record ResolveSubscribersRequest(
    Guid TenantId,
    SubscriptionScope Scope,
    /// <summary>The assignment's distinct grade-target ids — each grade's teachers are added to the
    /// cohort (fail-open per grade). Null/empty adds none.</summary>
    IReadOnlyList<Guid>? GradeLevelIds = null,
    IReadOnlyList<Guid>? StudentIds = null,
    /// <summary>The assignment's subject (FR-ER-13b). <c>null</c> = no subject = no eligibility hop.</summary>
    Guid? TopicId = null);
