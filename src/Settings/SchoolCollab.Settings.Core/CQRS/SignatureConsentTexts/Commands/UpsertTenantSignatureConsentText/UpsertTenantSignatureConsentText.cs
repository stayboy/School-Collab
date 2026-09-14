using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.SignatureConsentTexts.Commands.UpsertTenantSignatureConsentText;

/// <summary>
/// Creates or replaces the current tenant's guardian sign-off consent text
/// (WS-C2 / spec §3.2 line 53). Passing <see langword="null"/> restores the
/// embedded default. The returned <see cref="TenantSignatureConsentTextDto"/>
/// is the persisted text.
/// </summary>
public sealed record UpsertTenantSignatureConsentText(string? ConsentText) : ICommand;
