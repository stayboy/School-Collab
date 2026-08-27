using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.FeatureFlags.Commands;
using SchoolCollab.Settings.Core.CQRS.FeatureFlags.Queries;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Api.Endpoints;

/// <summary>
/// Value-valued tenant setting for the academic-year division
/// (period-hierarchy-terms-semesters.md FR-H6/H7). Lives under
/// <c>/api/config/flags/academic_year_division</c> (the Settings API's flag base
/// is <c>/api/config</c>; the spec's §7 used <c>/api/settings/feature-flags</c>
/// — the codebase base wins).
/// </summary>
public static class ConfigAcademicYearDivisionRoutes
{
    public static RouteGroupBuilder MapConfigAcademicYearDivisionRoutes(this RouteGroupBuilder group, bool requireFlagAdmin)
    {
        // GET /api/config/flags/academic_year_division — effective division for the current tenant.
        group.MapGet("/flags/academic_year_division", async (
            [FromServices] IQueryHandler<GetAcademicYearDivision, AcademicYearDivisionDto> handler,
            ITenantProvider tenants,
            CancellationToken ct) =>
        {
            try
            {
                var tenantId = tenants.GetTenantContext().TenantId;
                return Results.Ok(await handler.HandleAsync(new GetAcademicYearDivision(tenantId), ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // PUT /api/config/flags/academic_year_division — set the current tenant's division.
        group.MapPut("/flags/academic_year_division", async (
            [FromBody] SetDivisionRequest req,
            [FromServices] ICommandHandler<UpsertTenantFlagOverride, TenantFlagOverrideDto> handler,
            [FromServices] IQueryHandler<GetAcademicYearDivision, AcademicYearDivisionDto> getHandler,
            [FromServices] ISubPeriodCountProvider subPeriodCount,
            ITenantProvider tenants,
            CancellationToken ct) =>
        {
            if (!TryParseDivision(req.Value, out var normalized))
            {
                return Results.BadRequest(new { Message = "Value must be one of None | Terms | Semesters." });
            }

            var tenantId = tenants.GetTenantContext().TenantId;
            var current = await GetEffectiveDivisionAsync(getHandler, tenantId, ct);

            // FR-H7 (period-hierarchy-terms-semesters.md AC-H7/EC-H2): a framework
            // change is rejected while non-completed Term/Semester sub-periods exist
            // (the tenant must complete/remove them first). No-op writes (same value)
            // are always allowed.
            if (normalized != current)
            {
                try
                {
                    var count = await subPeriodCount.GetSubPeriodCountAsync(ct);
                    if (count > 0)
                    {
                        return Results.Json(new
                        {
                            Message = $"Cannot change academic-year division from '{current}' to '{normalized}': " +
                                      $"{count} sub-period(s) still exist. Complete or remove them first."
                        }, statusCode: 422);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    // Fail-closed: an indeterminate sub-period count must not allow a
                    // switch FR-H7 forbids while sub-periods exist.
                    return Results.Json(new { ex.Message }, statusCode: 422);
                }
            }

            try
            {
                await handler.HandleAsync(new UpsertTenantFlagOverride(
                    FeatureFlagKeys.AcademicYearDivision, tenantId, IsEnabled: null, normalized, req.Reason, null, null), ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { ex.Message }); }
        }).ApplyAdminPolicy(requireFlagAdmin);

        return group;
    }

    private static async Task<string> GetEffectiveDivisionAsync(
        IQueryHandler<GetAcademicYearDivision, AcademicYearDivisionDto> getHandler,
        Guid tenantId,
        CancellationToken ct)
    {
        try
        {
            return (await getHandler.HandleAsync(new GetAcademicYearDivision(tenantId), ct)).Value;
        }
        catch (KeyNotFoundException)
        {
            return "None";
        }
    }

    private static bool TryParseDivision(string value, out string normalized)
    {
        normalized = value?.Trim() ?? "";
        return normalized is "None" or "Terms" or "Semesters";
    }

    public sealed record SetDivisionRequest(string Value, string Reason);
}
