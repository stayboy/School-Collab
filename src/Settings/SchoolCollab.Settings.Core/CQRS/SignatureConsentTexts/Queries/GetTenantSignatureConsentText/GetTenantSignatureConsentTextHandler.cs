using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.SignatureConsentTexts.Queries.GetTenantSignatureConsentText;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.SignatureConsentTexts.Queries.GetTenantSignatureConsentText;

/// <summary>
/// Loads the current tenant's sign-off consent-text row via the tenant query
/// filter, returning the DTO or <see langword="null"/> when the tenant has not
/// configured one yet (WS-C2 / spec §3.2 line 53).
/// </summary>
public sealed class GetTenantSignatureConsentTextHandler(SettingsDbContext db)
    : IQueryHandler<GetTenantSignatureConsentText, TenantSignatureConsentTextDto?>
{
    public async Task<TenantSignatureConsentTextDto?> HandleAsync(
        GetTenantSignatureConsentText query, CancellationToken ct = default)
    {
        var row = await db.TenantSignatureConsentTexts
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        return new TenantSignatureConsentTextDto(row.ConsentText);
    }
}
