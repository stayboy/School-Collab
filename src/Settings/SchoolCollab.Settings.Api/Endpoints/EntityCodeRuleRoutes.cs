using Microsoft.AspNetCore.Mvc;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.ActivateEntityCodeRule;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.CreateEntityCodeRule;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.DeleteEntityCodeRule;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.ReplaceEntityCodeRuleOverrides;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.UpdateEntityCodeRule;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class EntityCodeRuleRoutes
{
    public static RouteGroupBuilder MapEntityCodeRuleRoutes(this RouteGroupBuilder group)
    {
        // List all rules (with segments).
        group.MapGet("/", async (
            [FromServices] IEntityCodeRuleRepository repository,
            CancellationToken ct) =>
        {
            var rules = await repository.ListAsync(ct);
            return Results.Ok(rules.Select(EntityCodeRuleDto.FromRule).ToList());
        });

        // Get a rule by id (with segments), ignoring soft-delete.
        group.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] IEntityCodeRuleRepository repository,
            CancellationToken ct) =>
        {
            var rule = await repository.GetByIdAsync(id, ct);
            return rule is null ? Results.NotFound() : Results.Ok(EntityCodeRuleDto.FromRule(rule));
        });

        // Get a rule by Code (for the generator lookup path; returns 404 if inactive/deleted).
        group.MapGet("/by-code/{code}", async (
            string code,
            [FromServices] IEntityCodeRuleRepository repository,
            CancellationToken ct) =>
        {
            var rule = await repository.GetActiveByCodeAsync(code, ct);
            return rule is null ? Results.NotFound() : Results.Ok(EntityCodeRuleDto.FromRule(rule));
        });

        // Create a new rule with segments.
        group.MapPost("/", async (
            [FromBody] CreateEntityCodeRule command,
            [FromServices] ICommandHandler<CreateEntityCodeRule, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/api/entity-code-rules/{id}", new { id });
            }
            catch (EntityCodeRuleCodeConflictException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        // Update an existing rule (replace-all segments).
        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateEntityCodeRuleRequest req,
            [FromServices] ICommandHandler<UpdateEntityCodeRule> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(
                    new UpdateEntityCodeRule(id, req.Name, req.Description, req.IsActive, req.Segments), ct);
                return Results.NoContent();
            }
            catch (EntityCodeRuleNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        // Soft-delete a rule.
        group.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] ICommandHandler<DeleteEntityCodeRule> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteEntityCodeRule(id), ct);
                return Results.NoContent();
            }
            catch (EntityCodeRuleNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // Activate a rule (deactivates any other active rule with the same Code).
        group.MapPost("/{id:guid}/activate", async (
            Guid id,
            [FromServices] ICommandHandler<ActivateEntityCodeRule> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new ActivateEntityCodeRule(id), ct);
                return Results.NoContent();
            }
            catch (EntityCodeRuleNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // ── Per-tenant overrides (spec §4.12) ──
        // Returns the current tenant's override rows for the given rule. The
        // rule itself is loaded once so the DTO can carry the per-row
        // SegmentIndex without a second round-trip on the client.
        group.MapGet("/{id:guid}/overrides", async (
            Guid id,
            [FromServices] IEntityCodeRuleRepository ruleRepository,
            [FromServices] ITenantEntityCodeRuleOverrideRepository overrideRepository,
            CancellationToken ct) =>
        {
            var rule = await ruleRepository.GetByIdAsync(id, ct);
            if (rule is null) return Results.NotFound();

            var rows = await overrideRepository.ListForRuleAsync(id, ct);
            var indexBySegmentId = rule.Segments.ToDictionary(s => s.Id, s => s.Index);
            var dtos = rows
                .Where(r => indexBySegmentId.ContainsKey(r.EntityCodeSegmentId))
                .Select(r => TenantEntityCodeRuleOverrideDto.FromOverride(r, indexBySegmentId[r.EntityCodeSegmentId]))
                .ToList();
            return Results.Ok(dtos);
        });

        // Replaces the current tenant's full override set on the rule
        // (atomic — single transaction, full overwrite).
        group.MapPut("/{id:guid}/overrides", async (
            Guid id,
            [FromBody] ReplaceOverridesRequest req,
            [FromServices] ICommandHandler<ReplaceEntityCodeRuleOverrides> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(
                    new ReplaceEntityCodeRuleOverrides(id, req.Overrides), ct);
                return Results.NoContent();
            }
            catch (EntityCodeRuleNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        return group;
    }
}

internal record UpdateEntityCodeRuleRequest(
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<EntityCodeSegmentInput> Segments);

internal record ReplaceOverridesRequest(
    IReadOnlyList<EntityCodeRuleOverrideInput> Overrides);