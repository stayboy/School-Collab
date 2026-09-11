using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Commands.UpsertTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Queries.GetTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class AssignmentPolicyRoutes
{
    /// <summary>
    /// Maps the per-tenant default guardian-signature policy at
    /// <c>/api/settings/assignment-policy</c> (WS-C1 / spec §7 Q1).
    /// </summary>
    public static RouteGroupBuilder MapAssignmentPolicyRoutes(this RouteGroupBuilder group)
    {
        // ── Get the tenant's default (204 when unset — caller falls back to false) ──
        group.MapGet("/assignment-policy", async (
            [FromServices] IQueryHandler<GetTenantAssignmentPolicy, TenantAssignmentPolicyDto?> handler,
            CancellationToken ct) =>
        {
            var policy = await handler.HandleAsync(GetTenantAssignmentPolicy.Instance, ct);
            return policy is null ? Results.NoContent() : Results.Ok(policy);
        });

        // ── Upsert (create or replace) the tenant's default ──
        group.MapPut("/assignment-policy", async (
            [FromBody] UpsertAssignmentPolicyRequest req,
            [FromServices] ICommandHandler<UpsertTenantAssignmentPolicy, TenantAssignmentPolicyDto> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new UpsertTenantAssignmentPolicy(req.RequiresSignatureDefault), ct);
            return Results.Ok(result);
        });

        return group;
    }

    public sealed record UpsertAssignmentPolicyRequest(bool RequiresSignatureDefault);
}