using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.SignatureConsentTexts.Commands.UpsertTenantSignatureConsentText;
using SchoolCollab.Settings.Core.CQRS.SignatureConsentTexts.Queries.GetTenantSignatureConsentText;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class SignatureConsentTextRoutes
{
    /// <summary>
    /// Maps the per-tenant guardian sign-off consent text at
    /// <c>/api/settings/signature-consent-text</c> (WS-C2 / spec §3.2 line 53).
    /// </summary>
    public static RouteGroupBuilder MapSignatureConsentTextRoutes(this RouteGroupBuilder group)
    {
        // ── Get the tenant's consent text (204 when unset — caller falls back to the embedded default) ──
        group.MapGet("/signature-consent-text", async (
            [FromServices] IQueryHandler<GetTenantSignatureConsentText, TenantSignatureConsentTextDto?> handler,
            CancellationToken ct) =>
        {
            var text = await handler.HandleAsync(GetTenantSignatureConsentText.Instance, ct);
            return text is null ? Results.NoContent() : Results.Ok(text);
        });

        // ── Upsert (create or replace) the tenant's consent text ──
        // The 4000-character cap throws a typed ArgumentException — mapped to
        // 400 with the message so the admin page can surface it.
        group.MapPut("/signature-consent-text", async (
            [FromBody] UpsertTenantSignatureConsentTextRequest req,
            [FromServices] ICommandHandler<UpsertTenantSignatureConsentText, TenantSignatureConsentTextDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(
                    new UpsertTenantSignatureConsentText(req.ConsentText), ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        return group;
    }

    public sealed record UpsertTenantSignatureConsentTextRequest(string? ConsentText);
}
