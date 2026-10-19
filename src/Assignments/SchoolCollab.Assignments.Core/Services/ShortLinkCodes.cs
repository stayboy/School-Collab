using System.Buffers.Text;
using System.Security.Cryptography;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.DeepLinks;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Round notification-templates-b2a (spec §3.6, Q61) — the <b>one</b> short-link code generator
/// and uniqueness loop, shared by both mint sites (the publish handler and the E3 sweep's
/// ensure-mint in <c>SweepNotificationQueuer</c>) so their collision posture cannot diverge.
///
/// <para>A code is a contact-scoped <b>credential</b>: it is 16 URL-safe characters
/// (<see cref="DeepLinkConstants.ShortLinkCodeLength"/>) rendered from 12
/// <see cref="RandomNumberGenerator"/> bytes (<see cref="Base64Url"/>, no padding), so it is never
/// derived from an id, a counter or the token — 96 bits of entropy and no guessable order.
/// Uniqueness is <b>global</b> (across tenants), because the public <c>/l/{code}</c> resolution is
/// unauthenticated and has no tenant to scope by: the single filtered unique index on
/// <c>AssignmentRecipient.ShortLinkCode</c> is the data-layer backstop, and
/// <see cref="MintUniqueAsync"/> re-rolls before an insert can ever hit it.</para>
/// </summary>
public static class ShortLinkCodes
{
    /// <summary>The random byte budget behind one code: 12 bytes render as exactly
    /// <see cref="DeepLinkConstants.ShortLinkCodeLength"/> base64url characters (no padding).</summary>
    private const int ByteLength = 12;

    /// <summary>How many candidates a mint may consume before giving up.</summary>
    public const int MaxAttempts = 5;

    /// <summary>A fresh, unverified candidate code.</summary>
    public static string NewCode() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength));

    /// <summary>
    /// Mints a code that <paramref name="isTaken"/> reports as unused, re-rolling on a collision
    /// (spec §3.6: "collision ⇒ re-roll"). The predicate is the caller's own global existence
    /// check, so both hosts mint against the same column and the same index.
    /// </summary>
    /// <param name="isTaken">Global (cross-tenant) existence check for a candidate code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An unused code.</returns>
    /// <exception cref="ShortLinkCodeCollisionException">No candidate was free in
    /// <see cref="MaxAttempts"/> attempts — a failure loud enough to be seen, never a duplicate.</exception>
    public static async Task<string> MintUniqueAsync(
        Func<string, CancellationToken, Task<bool>> isTaken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(isTaken);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate = NewCode();
            if (!await isTaken(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new ShortLinkCodeCollisionException();
    }
}
