using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
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
        group.MapGet("/", async Task<Ok<List<EntityCodeRuleDto>>> (
            [FromServices] IEntityCodeRuleRepository repository,
            CancellationToken ct) =>
        {
            var rules = await repository.ListAsync(ct);
            return TypedResults.Ok(rules.Select(EntityCodeRuleDto.FromRule).ToList());
        });

        // Get a rule by id (with segments), ignoring soft-delete.
        group.MapGet("/{id:guid}", async Task<Results<Ok<EntityCodeRuleDto>, NotFound>> (
            Guid id,
            [FromServices] IEntityCodeRuleRepository repository,
            CancellationToken ct) =>
        {
            var rule = await repository.GetByIdAsync(id, ct);
            return rule is null ? TypedResults.NotFound() : TypedResults.Ok(EntityCodeRuleDto.FromRule(rule));
        });

        // Get a rule by Code (for the generator lookup path; returns 404 if inactive/deleted).
        group.MapGet("/by-code/{code}", async Task<Results<Ok<EntityCodeRuleDto>, NotFound>> (
            string code,
            [FromServices] IEntityCodeRuleRepository repository,
            CancellationToken ct) =>
        {
            var rule = await repository.GetActiveByCodeAsync(code, ct);
            return rule is null ? TypedResults.NotFound() : TypedResults.Ok(EntityCodeRuleDto.FromRule(rule));
        });

        // Create a new rule with segments.
        group.MapPost("/", async Task<Results<Created<IdResponse>, BadRequest<MessageResponse>, Conflict<MessageResponse>>> (
            [FromBody] CreateEntityCodeRule command,
            [FromServices] ICommandHandler<CreateEntityCodeRule, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return TypedResults.Created($"/api/entity-code-rules/{id}", new IdResponse(id));
            }
            catch (EntityCodeRuleCodeConflictException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest(new MessageResponse(ex.Message));
            }
        });

        // Update an existing rule (replace-all segments).
        // Four outcome arms — NoContent, NotFound, BadRequest<MessageResponse>, Conflict<MessageResponse> —
        // which a Results<…> union would not keep readable, so this stays IResult by decision D19.
        group.MapPut("/{id:guid}", async Task<IResult> (
            Guid id,
            [FromBody] UpdateEntityCodeRuleRequest req,
            [FromServices] ICommandHandler<UpdateEntityCodeRule> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(
                    new UpdateEntityCodeRule(id, req.Name, req.Description, req.IsActive, req.Segments), ct);
                return TypedResults.NoContent();
            }
            catch (EntityCodeRuleNotFoundException)
            {
                return TypedResults.NotFound();
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest(new MessageResponse(ex.Message));
            }
            catch (ConcurrencyException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
        });

        // Soft-delete a rule.
        group.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (
            Guid id,
            [FromServices] ICommandHandler<DeleteEntityCodeRule> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteEntityCodeRule(id), ct);
                return TypedResults.NoContent();
            }
            catch (EntityCodeRuleNotFoundException)
            {
                return TypedResults.NotFound();
            }
        });

        // Activate a rule (deactivates any other active rule with the same Code).
        group.MapPost("/{id:guid}/activate", async Task<Results<NoContent, NotFound>> (
            Guid id,
            [FromServices] ICommandHandler<ActivateEntityCodeRule> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new ActivateEntityCodeRule(id), ct);
                return TypedResults.NoContent();
            }
            catch (EntityCodeRuleNotFoundException)
            {
                return TypedResults.NotFound();
            }
        });

        // ── Per-tenant overrides (spec §4.12) ──
        // Returns the current tenant's override rows for the given rule. The
        // rule itself is loaded once so the DTO can carry the per-row
        // SegmentIndex without a second round-trip on the client.
        group.MapGet("/{id:guid}/overrides",
            async Task<Results<Ok<List<TenantEntityCodeRuleOverrideDto>>, NotFound>> (
            Guid id,
            [FromServices] IEntityCodeRuleRepository ruleRepository,
            [FromServices] ITenantEntityCodeRuleOverrideRepository overrideRepository,
            CancellationToken ct) =>
        {
            var rule = await ruleRepository.GetByIdAsync(id, ct);
            if (rule is null) return TypedResults.NotFound();

            var rows = await overrideRepository.ListForRuleAsync(id, ct);
            var indexBySegmentId = rule.Segments.ToDictionary(s => s.Id, s => s.Index);
            var dtos = rows
                .Where(r => indexBySegmentId.ContainsKey(r.EntityCodeSegmentId))
                .Select(r => TenantEntityCodeRuleOverrideDto.FromOverride(r, indexBySegmentId[r.EntityCodeSegmentId]))
                .ToList();
            return TypedResults.Ok(dtos);
        });

        // Replaces the current tenant's full override set on the rule
        // (atomic — single transaction, full overwrite).
        group.MapPut("/{id:guid}/overrides",
            async Task<Results<NoContent, BadRequest<MessageResponse>, NotFound>> (
            Guid id,
            [FromBody] ReplaceOverridesRequest req,
            [FromServices] ICommandHandler<ReplaceEntityCodeRuleOverrides> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(
                    new ReplaceEntityCodeRuleOverrides(id, req.Overrides), ct);
                return TypedResults.NoContent();
            }
            catch (EntityCodeRuleNotFoundException)
            {
                return TypedResults.NotFound();
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest(new MessageResponse(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return TypedResults.BadRequest(new MessageResponse(ex.Message));
            }
        });

        // Generate a code from a rule + name hint (used by the topic-create
        // dialog's "regenerate template code" button). Advances the rule's
        // sequence state, so the caller should use the returned code directly
        // (not regenerate again on create).
        group.MapPost("/generate",
            async Task<Results<Ok<GeneratedCodeResponse>, NotFound<MessageResponse>>> (
            [FromBody] GenerateCodeRequest req,
            [FromServices] IEntityCodeGenerator generator,
            CancellationToken ct) =>
        {
            try
            {
                var code = await generator.GenerateWithNameAsync(req.RuleCode, req.NameHint, ct);
                return TypedResults.Ok(new GeneratedCodeResponse(code));
            }
            catch (EntityCodeRuleNotFoundException)
            {
                return TypedResults.NotFound(
                    new MessageResponse($"No active entity-code rule '{req.RuleCode}'."));
            }
        });

        return group;
    }
}

internal record GenerateCodeRequest(string RuleCode, string? NameHint);

internal record UpdateEntityCodeRuleRequest(
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<EntityCodeSegmentInput> Segments);

internal record ReplaceOverridesRequest(
    IReadOnlyList<EntityCodeRuleOverrideInput> Overrides);

/// <summary>200 body of <c>POST /api/entity-code-rules/generate</c> — exactly one <c>code</c> property.</summary>
internal sealed record GeneratedCodeResponse(string Code);
