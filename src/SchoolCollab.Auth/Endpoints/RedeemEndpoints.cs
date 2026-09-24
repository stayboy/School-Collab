using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Endpoints;

/// <summary>
/// <c>POST /auth/redeem</c> — the flag-ON handshake's redemption step (spec §5.2 / D6).
/// Redeems the single-use code and returns the **claim set only**: the response never carries
/// a token or a secret (D6 / AC9). The claim shape is built by <see cref="ClaimSetFactory"/> —
/// the ONE shape shared with the realm-mapper-driven paths, so the handshake path cannot drift
/// from the OIDC path (D9).
/// </summary>
public static class RedeemEndpoints
{
    /// <summary>Request body for <c>POST /auth/redeem</c>. <c>RedirectUri</c> is the caller's own
    /// return/callback URI — the same value the code was bound to at issue time. The calling app
    /// never receives — or holds — a session id: the session reference lives on the code entry
    /// (spec §5.2 steps 4-5), so the code alone resolves the pending claims.</summary>
    public sealed record RedeemRequest(string Code, string RedirectUri);

    public static IResult Redeem(
        RedeemRequest request,
        OneTimeCodeStore codes,
        PortalSessionStore sessions,
        ClaimSetFactory claims)
    {
        var redemption = codes.Redeem(request.Code, request.RedirectUri);
        return redemption.Status switch
        {
            // The session reference lives on the redeemed entry (bound at exchange, spec §5.2
            // step 4), so the caller supplies only the code and its own redirect URI.
            RedeemStatus.Success => ResolveClaims(redemption.Entry!.SessionId, sessions, claims),
            // A replay hits InvalidCode because the successful redemption already consumed the
            // entry; a redirect-URI mismatch is consumed as misuse by the store (D6).
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

    private static IResult ResolveClaims(string sessionId, PortalSessionStore sessions, ClaimSetFactory claims)
    {
        var entry = sessions.Get(sessionId);
        if (entry is null)
        {
            return Results.Json(
                new { error = "session_not_found" },
                statusCode: StatusCodes.Status404NotFound);
        }

        JsonElement payload;
        try
        {
            payload = DecodePayloadSegment(entry.IdToken);
        }
        catch (InvalidDataException)
        {
            // The ID token came from Keycloak through this service's own exchange — a payload
            // that does not decode as a well-formed JWT JSON object is an internal inconsistency.
            return Results.Json(
                new { error = "invalid_session_token" },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        try
        {
            return Results.Ok(claims.Build(payload));
        }
        catch (InvalidDataException ex)
        {
            // A required tenant/teacher claim is missing — the realm mappers did not emit the
            // contract shape this build expects (school-collab-realm.json).
            return Results.Json(
                new { error = "claim_set_incomplete", detail = ex.Message },
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Decodes the <c>payload</c> segment of a JWT as JSON **without cryptographic verification**.
    /// Deliberate for round A: the token was minted by Keycloak and stored by this service moments
    /// earlier during the direct-grant exchange, so the handshake path only needs the claim SHAPE
    /// (D9); full signature/issuer/audience validation and live parity are round B's work. A
    /// three-segment JWT with a valid base64url JSON payload is assumed; anything else is an
    /// internal inconsistency and throws.
    /// </summary>
    private static JsonElement DecodePayloadSegment(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidDataException("the ID token is not a three-segment JWT.");
        }

        var segment = (parts[1].Length % 4) switch
        {
            0 => parts[1],
            2 => parts[1] + "==",
            3 => parts[1] + "=",
            _ => throw new InvalidDataException("the ID token payload segment has an invalid length."),
        };

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(segment.Replace('-', '+').Replace('_', '/'));
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("the ID token payload segment is not valid base64url.", ex);
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("the ID token payload segment is not valid JSON.", ex);
        }
    }
}
