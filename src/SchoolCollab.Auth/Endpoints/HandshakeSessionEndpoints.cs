using Microsoft.AspNetCore.Http;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Endpoints;

/// <summary>
/// <c>POST /auth/handshake/session</c> — the session-yielding redemption (round
/// <c>portal-session-adoption</c> D3 / D19): a D6 handshake code plus its bound redirect URI in,
/// the caller's opaque session id out. It mirrors <see cref="PasskeyEndpoints.BootstrapRedeem"/>
/// but over the handshake <see cref="OneTimeCodeStore"/> instead of the bootstrap store, so the two
/// code namespaces stay disjoint exactly as built: a bootstrap code can never be redeemed here and
/// a handshake code can never be redeemed at <c>/auth/bootstrap/redeem</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this route exists.</b> <c>/auth/redeem</c> returns claims only and is documented never to
/// hand out a session id; <c>/auth/bootstrap/redeem</c> yields a session id but its codes are minted
/// only on the passkey path, bound to the single auth-portal bootstrap URI — a second portal cannot
/// land there. The D6 code, by contrast, is already minted bound to the caller's redirect URI and
/// carrying the session reference (<c>ExchangeEndpoints.Exchange</c>), so a session-yielding
/// redemption over the handshake store is the minimal generalization: no custody change, single-use
/// + TTL + URI-binding semantics inherited from the store.
/// </para>
/// <para>
/// <b>No allowlist check on this route — the allowlist is enforced at issuance time</b>
/// (<c>ExchangeEndpoints.Exchange</c> validates <c>return_uri</c> before minting, exactly as
/// <c>/auth/redeem</c> consumes already-allowlisted codes); the store's single-use + TTL +
/// <see cref="StringComparison.Ordinal"/> URI binding is the replay guard. Accepted consequence
/// (recorded): this route yields the session id to any holder of a valid code + its bound URI —
/// inside the AppHost trust domain (codes are single-use, short-TTL, and delivered only to the
/// bound callback), the same trust <c>/auth/bootstrap/redeem</c> already extends. The response
/// carries only the opaque session id — no token of any kind (AC11).
/// </para>
/// </remarks>
public static class HandshakeSessionEndpoints
{
    /// <summary>Request body — the <c>BootstrapRedeemRequest</c> spelling: <c>{code, redirectUri}</c>,
    /// where <c>redirectUri</c> is the URI the code was bound to when the exchange minted it.</summary>
    public sealed record HandshakeSessionRequest(string Code, string RedirectUri);

    /// <summary>Success body: the opaque session id — the value the caller sets as its own cookie.
    /// Token-free by design (AC11); the session's tokens stay in custody (D12).</summary>
    public sealed record HandshakeSessionResponse(string SessionId);

    /// <summary>
    /// Redeems a D6 handshake code for the opaque session id. The failure taxonomy is the
    /// <see cref="PasskeyEndpoints.BootstrapRedeem"/> one over the handshake store: a replay hits
    /// <c>invalid_code</c> (the successful redemption already consumed the entry), an elapsed TTL is
    /// <c>code_expired</c>, and a redirect-URI mismatch is consumed as misuse (<c>D6</c>) — every
    /// rejection fail-closed, and a bootstrap code is simply unknown here (namespace disjointness).
    /// A missing or blank <c>code</c> is not a redeemable value at all: it answers the same
    /// <c>invalid_code</c> 404, never a 500.
    /// </summary>
    public static IResult Redeem(HandshakeSessionRequest request, OneTimeCodeStore codes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(codes);

        if (string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return Results.Json(
                new { error = "redirect_uri_required" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            // An omitted/blank code is not a redeemable value at all: answer the taxonomy's
            // unknown-code shape rather than letting the store's null guard turn it into a 500.
            // (PasskeyEndpoints.BootstrapRedeem carries the same pre-existing hole — deliberately
            // NOT touched by this round; recorded in the round report.)
            return Results.Json(
                new { error = "invalid_code" },
                statusCode: StatusCodes.Status404NotFound);
        }

        var redemption = codes.Redeem(request.Code, request.RedirectUri);
        return redemption.Status switch
        {
            RedeemStatus.Success => Results.Ok(new HandshakeSessionResponse(redemption.Entry!.SessionId)),
            RedeemStatus.InvalidCode => Results.Json(
                new { error = "invalid_code" },
                statusCode: StatusCodes.Status404NotFound),
            RedeemStatus.Expired => Results.Json(
                new { error = "code_expired" },
                statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Json(
                new { error = "redirect_uri_mismatch" },
                statusCode: StatusCodes.Status400BadRequest),
        };
    }
}
