using System.Text.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// The Assignments API's <see cref="IPortalSessionClaimsReader"/> (round
/// <c>portal-session-adoption</c> D4 / D19): the moved portal-session scheme needs a claims read,
/// and custody is process-local in the auth service — so this host implements the Core port as a
/// remote HTTP read against <c>GET /auth/session/{id}/claims</c> (D2) through the cross-module
/// client. The auth service answers from its D15 seam's cheap, I/O-free projection — no refresh,
/// no Keycloak round trip — so the remote path keeps the handler's documented invariant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fail-closed everywhere (the ar-24 posture):</b> a transport failure, a non-2xx answer (the
/// unknown-session <c>404</c> and an auth-service outage alike), a non-JSON body (a 2xx HTML login
/// page is the exact fail-open class ar-24 closed with <c>AllowAutoRedirect=false</c>) and a body
/// without the pinned claim contract all yield <c>null</c> — the portal-session handler turns that
/// into <see cref="AuthenticateResult.NoResult"/> and a bare 401. An auth-service outage therefore
/// 401s portal callers (the portal renders its session-ended card); it never fails open.
/// </para>
/// <para>
/// The host's <c>Program.cs</c> registers this typed client via
/// <c>AddCrossModuleHttpClient&lt;PortalSessionClaimsReader&gt;</c> (retry handler + long handler
/// lifetime, the literal <c>https+http://auth</c> base address at the call site, tenant propagation
/// off — the identity of the read IS the session id in the URL) and then forwards
/// <see cref="IPortalSessionClaimsReader"/> to it with its own registration line: the typed-client
/// helper binds the concrete class alone, and the portal-session handler resolves the interface from
/// DI. The literal base address is matched by the AppHost's <c>.WithReference(auth)</c> on
/// <c>assignments-api</c> per <c>CrossModuleWiringTests</c>.
/// </para>
/// </remarks>
public sealed class PortalSessionClaimsReader(HttpClient httpClient, ILogger<PortalSessionClaimsReader> logger)
    : IPortalSessionClaimsReader
{
    /// <summary>The auth service's cheap claims route (D2).</summary>
    public const string ClaimsPath = "/auth/session/{sessionId}/claims";

    /// <summary>
    /// The required claims as (wire key, claim name) pairs — the D2 body carries them under its
    /// <b>own</b> spelling, which is NOT the <see cref="PortalClaims"/> claim name: the auth route
    /// serializes <c>SessionClaimsResponse</c> through minimal APIs' Web defaults, so every key is
    /// camelCase. The claim names stay the realm-mapper spelling <c>BuildPrincipal</c>
    /// materializes; this table is the one place the two spellings meet. A read that looked the
    /// claim names up in the body would refuse every real session (fail-closed but feature-dead),
    /// which is exactly what <c>PortalSessionReaderPolicyTests</c> pins now.
    /// </summary>
    private static readonly (string WireKey, string ClaimName)[] RequiredStringFields =
    [
        ("tenantId", PortalClaims.TenantIdClaim),
        ("tenantName", PortalClaims.TenantNameClaim),
        ("tenantType", PortalClaims.TenantTypeClaim),
        ("teacherId", PortalClaims.TeacherIdClaim),
    ];

    /// <inheritdoc />
    public async ValueTask<PortalClaims?> ReadClaimsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var path = ClaimsPath.Replace("{sessionId}", Uri.EscapeDataString(sessionId), StringComparison.Ordinal);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(path, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // Transport failure — including the request-aborted race a caller cancelling induces.
            // An outage must 401 the caller, never fail open.
            logger.LogWarning(error, "The auth service's claims read is unreachable; failing closed.");
            return null;
        }

        var content = response.Content;
        if (!response.IsSuccessStatusCode || !IsJson(content))
        {
            // Non-2xx (unknown session 404, outage 502) and a non-JSON 2xx (an OIDC challenge that
            // answered with an HTML login page — never data) share the one fail-closed answer.
            logger.LogWarning(
                "The auth service's claims read answered HTTP {StatusCode} {ContentType}; failing closed.",
                (int)response.StatusCode,
                content.Headers.ContentType?.ToString() ?? "no content-type");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(await content.ReadAsStringAsync(cancellationToken));
            return FromPayload(document.RootElement);
        }
        catch (JsonException error)
        {
            logger.LogWarning(error, "The auth service's claims read was not valid JSON; failing closed.");
            return null;
        }
    }

    /// <summary>Maps the D2 response body onto the D9 claim set, or <c>null</c> when the shape does
    /// not carry the pinned contract (the same refusal the auth-service adapter's own custody read
    /// applies for a token payload without the claim names).</summary>
    private static PortalClaims? FromPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var strings = new Dictionary<string, string>(RequiredStringFields.Length, StringComparer.Ordinal);
        foreach (var (wireKey, claimName) in RequiredStringFields)
        {
            if (!TryGetString(payload, wireKey, out var value))
            {
                return null;
            }

            strings[claimName] = value;
        }

        // roles is the one field whose wire key and D9 claim name coincide, so it needs no table
        // entry — it is read under the same spelling either way.
        var roles = new List<string>();
        if (payload.TryGetProperty(PortalClaims.RolesClaim, out var rolesValue)
            && rolesValue.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in rolesValue.EnumerateArray())
            {
                if (role.ValueKind == JsonValueKind.String)
                {
                    roles.Add(role.GetString() ?? string.Empty);
                }
            }
        }
        else if (rolesValue.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            return null;
        }

        return new PortalClaims(
            strings[PortalClaims.TenantIdClaim],
            strings[PortalClaims.TenantNameClaim],
            strings[PortalClaims.TenantTypeClaim],
            strings[PortalClaims.TeacherIdClaim],
            roles);
    }

    private static bool IsJson(HttpContent content) =>
        (content.Headers.ContentType?.MediaType ?? string.Empty)
            .StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetString(JsonElement payload, string field, out string value)
    {
        value = string.Empty;
        if (!payload.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
    }
}
