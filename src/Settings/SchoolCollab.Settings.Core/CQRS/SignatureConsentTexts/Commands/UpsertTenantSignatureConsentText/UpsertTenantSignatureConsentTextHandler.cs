using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.SignatureConsentTexts.Commands.UpsertTenantSignatureConsentText;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.SignatureConsentTexts.Commands.UpsertTenantSignatureConsentText;

/// <summary>
/// Upserts the single sign-off consent-text row for the current tenant. The
/// tenant query filter scopes reads to the current tenant; the row is created
/// when absent (WS-C2 / spec §3.2 line 53). The 4000-character cap is enforced
/// by <see cref="TenantSignatureConsentText.SetConsentText"/> (typed
/// <see cref="ArgumentException"/>, mapped to 400 at the route).
/// </summary>
public sealed class UpsertTenantSignatureConsentTextHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider) : ICommandHandler<UpsertTenantSignatureConsentText, TenantSignatureConsentTextDto>
{
    public async Task<TenantSignatureConsentTextDto> HandleAsync(
        UpsertTenantSignatureConsentText command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var existing = await db.TenantSignatureConsentTexts.SingleOrDefaultAsync(ct);
        TenantSignatureConsentText row;
        if (existing is not null)
        {
            existing.SetConsentText(command.ConsentText);
            row = existing;
        }
        else
        {
            row = TenantSignatureConsentText.Create(tenantId, command.ConsentText);
            db.TenantSignatureConsentTexts.Add(row);
        }

        await db.SaveChangesAsync(ct);

        return new TenantSignatureConsentTextDto(row.ConsentText);
    }
}
