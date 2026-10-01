using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Commands.UpsertTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Queries.GetTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class AssignmentPolicyRoutes
{
    /// <summary>
    /// Maps the per-tenant default assignment policy at
    /// <c>/api/settings/assignment-policy</c> (WS-C1 / spec §7 Q1;
    /// <c>documents/solution/assignment-policy-fields.md</c> §4).
    /// </summary>
    public static RouteGroupBuilder MapAssignmentPolicyRoutes(this RouteGroupBuilder group)
    {
        // ── Get the tenant's default (204 when unset — caller applies built-in defaults) ──
        group.MapGet("/assignment-policy", async (
            [FromServices] IQueryHandler<GetTenantAssignmentPolicy, TenantAssignmentPolicyDto?> handler,
            CancellationToken ct) =>
        {
            var policy = await handler.HandleAsync(GetTenantAssignmentPolicy.Instance, ct);
            return policy is null ? Results.NoContent() : Results.Ok(policy);
        });

        // ── Upsert (create or replace) the tenant's default ──
        // A non-positive contact cap is rejected by the handler (Q7) and mapped to
        // 400 here; every other failure stays a 500.
        group.MapPut("/assignment-policy", async (
            [FromBody] UpsertAssignmentPolicyRequest req,
            [FromServices] ICommandHandler<UpsertTenantAssignmentPolicy, TenantAssignmentPolicyDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(
                    new UpsertTenantAssignmentPolicy(
                        req.ResolveSignatureRequirement(),
                        req.RequiresApprovalBeforePublish,
                        req.MaxPrimaryContacts,
                        req.MaxCopyContacts), ct);
                return Results.Ok(result);
            }
            catch (ArgumentOutOfRangeException ex) { return Results.BadRequest(new { ex.Message }); }
        });

        return group;
    }

    /// <summary>
    /// PUT body for the tenant-global assignment policy. Every field is nullable: null stores
    /// "unset" (the built-in default then applies).
    ///
    /// <para><b>Legacy-input compatibility (Round A only).</b> The pre-widening wire shape was the
    /// input-only boolean <c>requiresSignatureDefault</c>, and the shipped Admin editor
    /// (<c>GradeSignaturePolicyEditor</c>) still sends it. It is therefore accepted <i>alongside</i>
    /// the new field set and mapped when <see cref="SignatureRequirement"/> is absent
    /// (<c>true → Optional</c>, <c>false → Disabled</c>, null → unset). Round B deletes this member
    /// together with the retired editor.</para>
    /// </summary>
    public sealed record UpsertAssignmentPolicyRequest(
        SignatureRequirementMode? SignatureRequirement,
        bool? RequiresApprovalBeforePublish,
        int? MaxPrimaryContacts,
        int? MaxCopyContacts,
        bool? RequiresSignatureDefault = null)
    {
        /// <summary>
        /// The signature requirement to persist: the new field when supplied, else the legacy
        /// boolean mapped per D8's <c>false → Disabled, true → Optional</c> back-compat rule.
        /// </summary>
        public SignatureRequirementMode? ResolveSignatureRequirement() =>
            SignatureRequirement ?? (RequiresSignatureDefault switch
            {
                true => SignatureRequirementMode.Optional,
                false => SignatureRequirementMode.Disabled,
                null => (SignatureRequirementMode?)null,
            });
    }
}
