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
/// </summary>
public sealed record SubscriberInfo(
    Guid ContactId,
    ContactOwnerType OwnerType,
    Guid OwnerId,
    Guid? StudentId,
    ContactChannel Channel,
    GuardianRole? Role);

/// <summary>
/// Request to resolve publish recipients. Provide an explicit <see cref="StudentIds"/>
/// roster, or a <see cref="GradeLevelId"/> to resolve the whole cohort (the
/// resolver enumerates students by grade, their guardians, and each owner's
/// subscribed contacts). When neither is supplied the result is empty.
/// </summary>
public sealed record ResolveSubscribersRequest(
    Guid TenantId,
    SubscriptionScope Scope,
    Guid? GradeLevelId = null,
    IReadOnlyList<Guid>? StudentIds = null);
