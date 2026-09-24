using System.IO;
using System.Text.Json;

namespace SchoolCollab.Auth.Services;

/// <summary>The portal-facing claim set (spec §5.4 / D9). ONE shape, ONE factory: the Direct-Grant
/// handshake path and the realm-mapper-driven paths must not drift apart, so the claim names here
/// ARE the mapper contract pinned by the realm import (school-collab-realm.json).</summary>
public sealed record PortalClaims(
    string TenantId,
    string TenantName,
    string TenantType,
    string TeacherId,
    IReadOnlyList<string> Roles);

/// <summary>
/// Builds the claim set from a decoded Keycloak token payload. The caller owns token validation
/// and JWT decoding; this class is purely about claim SHAPE, so a single fixture pins both paths
/// to the same names (D9). The tenant/teacher claims are REQUIRED — a session without them cannot
/// bind to a tenant, so a missing claim fails fast naming the claim rather than degrading.
/// <c>roles</c> is multivalued (the realm's <c>User Realm Role</c> mapper sets
/// <c>multivalued: true</c>); its absence means no roles.
/// </summary>
public sealed class ClaimSetFactory
{
    // These names are the realm mapper contract — changing the realm mappers or these constants
    // without the other is exactly the drift D9 forbids (school-collab-realm.json mappers:
    // tenant-id → tenant_id, tenant-name → tenant_name, tenant-type → tenant_type,
    // teacher-id → teacher_id, roles → roles).
    public const string TenantIdClaim = "tenant_id";
    public const string TenantNameClaim = "tenant_name";
    public const string TenantTypeClaim = "tenant_type";
    public const string TeacherIdClaim = "teacher_id";
    public const string RolesClaim = "roles";

    /// <summary>
    /// <paramref name="payload"/> must be a JSON object — a decoded token payload (the ID token
    /// on the handshake path; the same shape the realm's attribute and role mappers emit on both
    /// paths).
    /// </summary>
    /// <exception cref="InvalidDataException">The payload is not an object, a required
    /// tenant/teacher claim is missing or not textual, or <c>roles</c> is malformed.</exception>
    public PortalClaims Build(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("the token payload must be a JSON object.");
        }

        var tenantId = GetRequiredString(payload, TenantIdClaim);
        var tenantName = GetRequiredString(payload, TenantNameClaim);
        var tenantType = GetRequiredString(payload, TenantTypeClaim);
        var teacherId = GetRequiredString(payload, TeacherIdClaim);
        var roles = GetRoles(payload);

        return new PortalClaims(tenantId, tenantName, tenantType, teacherId, roles);
    }

    private static string GetRequiredString(JsonElement payload, string claim)
    {
        if (!payload.TryGetProperty(claim, out var value))
        {
            throw new InvalidDataException(
                $"the token payload is missing the required '{claim}' claim — the realm mapper "
                + "contract (school-collab-realm.json) provides it on both the ID and access tokens.");
        }

        // The realm's attribute mappers are NOT multivalued, so the token carries a plain JSON
        // string; a single-element array is accepted defensively in case a mapper is ever flipped.
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        if (value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() == 1
            && value[0].ValueKind == JsonValueKind.String)
        {
            return value[0].GetString() ?? string.Empty;
        }

        throw new InvalidDataException(
            $"the '{claim}' claim must be a string (or a single-element string array), was {value.ValueKind}.");
    }

    private static IReadOnlyList<string> GetRoles(JsonElement payload)
    {
        if (!payload.TryGetProperty(RolesClaim, out var value))
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"the '{RolesClaim}' claim must be an array of strings (the realm mapper sets "
                + $"multivalued: true), was {value.ValueKind}.");
        }

        var roles = new List<string>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"the '{RolesClaim}' claim contains a non-string entry.");
            }
            roles.Add(item.GetString() ?? string.Empty);
        }
        return roles;
    }
}
