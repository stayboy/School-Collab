namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// Tenant-level guardian sign-off consent text (WS-C2 / spec §3.2 line 53).
/// <see langword="null"/> <see cref="ConsentText"/> = the embedded default applies.
/// </summary>
public sealed record TenantSignatureConsentTextDto(string? ConsentText);
