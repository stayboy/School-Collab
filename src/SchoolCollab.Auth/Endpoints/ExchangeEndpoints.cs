using System.Threading;
using Microsoft.AspNetCore.Http;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Endpoints;

/// <summary>
/// <c>POST /auth/exchange</c> — the flag-ON handshake's credential step (spec §5.2 / D6),
/// called **server-to-server by the portal inside the AppHost network, never from a browser**
/// (spec §14 trust boundary). It exchanges Keycloak credentials (Direct Grant) for a server-side
/// portal session plus a single-use handshake code bound to the originating app's redirect URI.
/// The code entry carries the session reference (spec §5.2 step 4), so the app that
/// redeems the code resolves the claims from the code alone — it never receives or holds
/// a session id or a token.
/// The success response carries NO token: only the opaque session id and the one-time code. The
/// token set lives solely in <see cref="PortalSessionStore"/> (D12) and never reaches a response
/// body, a log line or an exception message (D7 / AC9).
/// <para>
/// The supplied <c>RedirectUri</c> is checked against the per-app allowlist
/// (<see cref="AppCallbackAllowlist"/>, round B pass B5b) <b>before anything else happens</b> —
/// before the upstream Direct-Grant call and therefore before any code exists. A crafted
/// <c>portal/login?return_uri=attacker</c> link is defused here rather than at redemption: URI
/// binding cannot defend a URI the attacker chooses, so the code must never be minted for one.
/// </para>
/// </summary>
public static class ExchangeEndpoints
{
    /// <summary>Request body for <c>POST /auth/exchange</c>. <c>RedirectUri</c> is the
    /// originating app's URI the one-time code is bound to (the redemption must supply
    /// the identical value) — and that must match the per-app allowlist, or no code is
    /// minted at all (round B pass B5b).</summary>
    public sealed record ExchangeRequest(string Username, string Password, string RedirectUri);

    /// <summary>Success body. Token-free by design: only the single-use code and the opaque
    /// session id the caller needs to redeem and to revoke the session later.</summary>
    public sealed record ExchangeResponse(string Code, string SessionId, int ExpiresInSeconds);

    /// <summary>
    /// Handles the exchange. The Direct-Grant failure taxonomy (pass 3a) is surfaced so the
    /// portal can present the right message; <c>Detail</c> is Keycloak's own
    /// <c>error_description</c> when the body parses, otherwise a short bounded message —
    /// never the raw upstream body and never a token. The two redirect-URI guards reuse that
    /// taxonomy's 400: a blank value is <c>redirect_uri_required</c>, a value outside the
    /// allowlist is <c>redirect_uri_not_allowed</c>. Neither body carries a code (there is none
    /// to carry) and neither echoes the rejected URI.
    /// </summary>
    public static async Task<IResult> Exchange(
        ExchangeRequest request,
        AppCallbackAllowlist callbackAllowlist,
        DirectGrantExchanger exchanger,
        PortalSessionStore sessions,
        OneTimeCodeStore codes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return Results.Json(
                new { error = "redirect_uri_required" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The allowlist is enforced at ISSUANCE and ahead of the credential exchange: a
        // non-allowlisted target must not receive a code, and must not even cost a Keycloak
        // round trip. Fail-closed — AppCallbackAllowlist denies anything it cannot match.
        if (!callbackAllowlist.IsAllowed(request.RedirectUri))
        {
            return Results.Json(
                new { error = "redirect_uri_not_allowed" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await exchanger.ExchangeAsync(request.Username, request.Password, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Status switch
            {
                DirectGrantStatus.InvalidCredentials => Results.Json(
                    new { error = "invalid_credentials", detail = result.Detail },
                    statusCode: StatusCodes.Status401Unauthorized),
                DirectGrantStatus.DisabledUser => Results.Json(
                    new { error = "disabled_user", detail = result.Detail },
                    statusCode: StatusCodes.Status403Forbidden),
                _ => Results.Json(
                    new { error = "keycloak_unreachable", detail = result.Detail },
                    statusCode: StatusCodes.Status502BadGateway),
            };
        }

        // The token set exists ONLY in the custody store; the caller receives the opaque
        // session id plus the single-use code — never a token. Keycloak's ROPC + openid
        // response always carries all three tokens, so a "success" that omits id_token or
        // refresh_token is a malformed endpoint (same family as a 200 with no access_token)
        // and must not seed a custody entry that redemption or logout could never use.
        if (result.IdToken is null || result.RefreshToken is null)
        {
            return Results.Json(
                new { error = "keycloak_unreachable", detail = "the token endpoint omitted id_token or refresh_token" },
                statusCode: StatusCodes.Status502BadGateway);
        }

        var sessionId = sessions.Create(
            result.AccessToken!, result.RefreshToken, result.IdToken, result.ExpiresInSeconds);
        var code = codes.Create(request.RedirectUri, sessionId);

        return Results.Ok(new ExchangeResponse(code, sessionId, result.ExpiresInSeconds));
    }
}
