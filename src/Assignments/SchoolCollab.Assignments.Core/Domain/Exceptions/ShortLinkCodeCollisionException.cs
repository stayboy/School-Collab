namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// Round notification-templates-b2a (spec §3.6, Q61) — thrown when
/// <see cref="Services.ShortLinkCodes.MintUniqueAsync"/> cannot find an unused code within
/// <see cref="Services.ShortLinkCodes.MaxAttempts"/> re-rolls. Typed (never
/// <see cref="InvalidOperationException"/>) because a credential that cannot be minted must
/// surface as a named failure rather than as a silent duplicate the global unique index would
/// later reject at save time.
/// </summary>
public sealed class ShortLinkCodeCollisionException()
    : Exception($"Could not mint a unique short-link code in {Services.ShortLinkCodes.MaxAttempts} attempts.");
