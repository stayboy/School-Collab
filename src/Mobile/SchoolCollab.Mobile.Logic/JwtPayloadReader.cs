using System.Text.Json;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// Decodes the claim values out of a JWT's payload segment. One entry per JSON property, one
/// value per claim — a multivalued claim (<c>roles</c>) therefore reads as one claim per array
/// element, which is the shape the shared gates consume.
/// </summary>
/// <remarks>
/// <para><b>Signature verification is deliberately absent.</b> On the device the token is an
/// opaque bearer credential: verification is the API's job (<c>AddJwtBearer</c> validates issuer,
/// audience and signature), and the claims read here only drive UI visibility
/// (<c>TenantGate</c>/<c>FeatureFlagGate</c> are UI gating, never enforcement). A forged token
/// therefore buys nothing — every endpoint still refuses it — while verifying client-side would
/// mean shipping the realm's public key inside the app. <c>JwtPayloadReaderTests</c> pins this
/// absence: a token whose signature segment is garbage still decodes its claims.</para>
/// <para>The claim <i>spellings</i> are whatever the realm emits (<c>tenant_id</c>,
/// <c>tenant_name</c>, <c>roles</c>) — the shared UI reads them verbatim
/// (<c>VisibleTenantService</c>'s <c>user.FindFirst("tenant_id")</c>), so nothing is renamed to
/// <c>ClaimTypes.*</c> here.</para>
/// </remarks>
public static class JwtPayloadReader
{
    /// <summary>A JWT is <c>header.payload.signature</c>.</summary>
    private const int SegmentCount = 3;

    /// <summary>The payload is segment index 1 (header is 0, signature is 2).</summary>
    private const int PayloadSegmentIndex = 1;

    /// <summary>
    /// Reads every claim from <paramref name="token"/>'s payload. Values that are JSON objects
    /// (rather than strings, numbers, booleans or arrays of those) are not claim values and are
    /// skipped.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="token"/> is null, empty or whitespace.</exception>
    /// <exception cref="FormatException">The token is not a three-segment JWT, its payload segment
    /// is not base64url, or the payload is not a JSON object.</exception>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadClaimValues(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var segments = token.Split('.');
        if (segments.Length != SegmentCount)
        {
            throw new FormatException(
                $"A JWT must carry exactly {SegmentCount} '.'-separated segments; got {segments.Length}.");
        }

        var payload = DecodeBase64Url(segments[PayloadSegmentIndex]);
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"A JWT payload must be a JSON object; got {document.RootElement.ValueKind}.");
        }

        var claims = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var values = new List<string>();
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    values.Add(property.Value.GetString()!);
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    values.Add(property.Value.GetRawText());
                    break;
                case JsonValueKind.Array:
                    values.AddRange(property.Value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!));
                    break;
                default:
                    continue;
            }

            claims[property.Name] = values;
        }

        return claims;
    }

    private static ReadOnlyMemory<byte> DecodeBase64Url(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            0 => padded,
            _ => throw new FormatException("A JWT payload segment is not valid base64url."),
        };

        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException ex)
        {
            throw new FormatException("A JWT payload segment is not valid base64url.", ex);
        }
    }
}
