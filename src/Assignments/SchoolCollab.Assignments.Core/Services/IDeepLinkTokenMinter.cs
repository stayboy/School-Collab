using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>Result of minting a contact-scoped deep-link token: the protected bearer
/// token string plus the moment it expires.</summary>
public sealed record DeepLinkMintedToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Mint point for the contact-scoped deep-link tokens (WS-E1 / ar-14-deep-links).
/// The publish flow calls <see cref="Mint"/> when it persists a recipient (new mint)
/// or re-mints an expired/absent token on republish. Minting is deliberately
/// independent of the <c>FEATURE:EnableDeepLinks</c> runtime flag — tokens are always
/// minted at publish (cheap, idempotent) so no republish is needed once the public
/// landing is dark-launched on.
/// </summary>
public interface IDeepLinkTokenMinter
{
    /// <summary>Mints a token for <paramref name="recipient"/> with expiry
    /// <paramref name="now"/> + <paramref name="linkValidityDays"/> (falling back to
    /// <see cref="DeepLinkConstants.DefaultValidityDays"/> when null).</summary>
    DeepLinkMintedToken Mint(AssignmentRecipient recipient, int? linkValidityDays, DateTimeOffset now);
}
