using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace SchoolCollab.Core.DeepLinks;

/// <summary>
/// Thin wrapper over a purpose-scoped <see cref="IDataProtector"/> for the contact
/// deep-link tokens (ar-14-deep-links). <see cref="Protect"/> mints opaque bearer
/// ciphertext from the <see cref="DeepLinkTokenPayload"/>; <see cref="TryUnprotect"/>
/// reverses it and returns <c>null</c> when the token is tampered with, malformed, or
/// produced under a different purpose — never throwing out of the wrapper, so the
/// public landing can treat any bad token as an expired link.
/// </summary>
public sealed class DeepLinkProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector(DeepLinkConstants.Purpose);

    /// <summary>Serialises and protects a payload into the opaque bearer token string.</summary>
    public string Protect(DeepLinkTokenPayload payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return _protector.Protect(json);
    }

    /// <summary>Unprotects a token back to its payload, or <c>null</c> when the token is
    /// invalid, tampered with, or created under a different purpose.</summary>
    public DeepLinkTokenPayload? TryUnprotect(string token)
    {
        try
        {
            var json = _protector.Unprotect(token);
            return JsonSerializer.Deserialize<DeepLinkTokenPayload>(json);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}
