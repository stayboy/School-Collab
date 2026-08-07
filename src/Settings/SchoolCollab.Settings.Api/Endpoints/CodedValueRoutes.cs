using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateProvisionalCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.ApproveProvisionalCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RejectProvisionalCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.DeleteCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.DisableCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.EnableCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RecoverCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueOverride;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpdateCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpsertCodedValueOverride;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueByCode;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueById;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValuesByIds;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValuesByParent;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.ListProvisionalCodedValues;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.ListRootCodedValues;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.SearchCodedValues;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class CodedValueRoutes
{
    public static RouteGroupBuilder MapCodedValueRoutes(this RouteGroupBuilder group)
    {
        // ── Coded Values (search / lookup / CRUD / lifecycle) ───────────────────

        group.MapGet("/search", async (
            [FromQuery] string text,
            [FromQuery] Guid? parentId,
            [FromQuery] bool? includeDisabled,
            [FromServices] IQueryHandler<SearchCodedValues, CodedValueDto[]> handler,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(text))
                return Results.Ok(Array.Empty<CodedValueDto>());

            var result = await handler.HandleAsync(
                new SearchCodedValues(text, parentId, includeDisabled ?? false), ct);
            return Results.Ok(result);
        });

        group.MapGet("/", async (
            [FromServices] IQueryHandler<ListRootCodedValues, CodedValueDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListRootCodedValues(), ct)));

        group.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] IQueryHandler<GetCodedValueById, CodedValueDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCodedValueById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/by-code/{code}", async (
            string code,
            [FromQuery] Guid? parentId,
            [FromServices] IQueryHandler<GetCodedValueByCode, CodedValueDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCodedValueByCode(code, parentId), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/by-ids", async (
            [FromQuery] Guid[] ids,
            [FromServices] IQueryHandler<GetCodedValuesByIds, CodedValueDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new GetCodedValuesByIds(ids), ct)));

        group.MapGet("/by-parent", async (
            [FromQuery] Guid? parentId,
            [FromQuery] string? parentCode,
            [FromQuery] string? attributeKey,
            [FromQuery] string? attributeValue,
            [FromQuery] bool? includeDisabled,
            [FromServices] IQueryHandler<GetCodedValuesByParent, CodedValueDto[]> handler,
            CancellationToken ct) =>
        {
            IReadOnlyDictionary<string, string>? filters = null;
            if (!string.IsNullOrWhiteSpace(attributeKey) && !string.IsNullOrWhiteSpace(attributeValue))
            {
                filters = new Dictionary<string, string> { [attributeKey] = attributeValue };
            }

            return Results.Ok(await handler.HandleAsync(
                new GetCodedValuesByParent(parentId, parentCode, filters, includeDisabled ?? false), ct));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] ICommandHandler<DeleteCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteCodedValue(id), ct);
                return Results.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return Results.NotFound();
            }
            catch (CodedValueHasChildrenException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
            catch (CodedValueReferencedException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPost("/{id:guid}/recover", async (
            Guid id,
            [FromServices] ICommandHandler<RecoverCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RecoverCodedValue(id), ct);
                return Results.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapGet("/deleted", async (
            [FromServices] ICodedValueRepository repository,
            CancellationToken ct) =>
            Results.Ok(await repository.ListDeletedAsync(ct)));

        group.MapPost("/", async (
            [FromBody] CreateCodedValue command,
            [FromServices] ICommandHandler<CreateCodedValue, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/api/coded-values/{id}", new { id });
            }
            catch (CodedValueCodeConflictException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPost("/bulk", async (
            [FromBody] BulkCreateCodedValuesRequest req,
            [FromServices] ICommandHandler<BulkCreateCodedValues, BulkCreateResult> handler,
            CancellationToken ct) =>
        {
            try
            {
                var command = new BulkCreateCodedValues(
                    req.ParentId,
                    req.Children.Select(c => new BulkCreateChildItem(c.Code, c.Name, c.Description, c.DisplayOrder)).ToList());
                var result = await handler.HandleAsync(command, ct);
                return Results.Ok(new { result.ParentId, result.CreatedCount, result.SkippedCodes });
            }
            catch (CodedValueNotFoundException)
            {
                return Results.NotFound(new { Message = $"Parent coded value with ID '{req.ParentId}' not found." });
            }
            catch (DuplicateCodeException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateCodedValueRequest req,
            [FromServices] ICommandHandler<UpdateCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateCodedValue(id, req.Name, req.Description, req.DisplayOrder), ct);
                return Results.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPost("/{id:guid}/disable", async (
            Guid id,
            [FromServices] ICommandHandler<DisableCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DisableCodedValue(id), ct);
                return Results.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/{id:guid}/enable", async (
            Guid id,
            [FromServices] ICommandHandler<EnableCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new EnableCodedValue(id), ct);
                return Results.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // ── Tenant coded-value overrides (current tenant from ITenantProvider) ──────
        // PUT creates/updates the override; DELETE removes it (idempotent). The
        // handler resolves the current tenant from ITenantProvider (not the URL),
        // so these are the per-tenant display-name overrides consumed by the
        // wizard's override dialog (§6.2). See spec §5.1.
        group.MapPut("/{id:guid}/override", async (
            Guid id,
            [FromBody] UpsertOverrideRequest req,
            [FromServices] ICommandHandler<UpsertCodedValueOverride, CodedValueDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var dto = await handler.HandleAsync(
                    new UpsertCodedValueOverride(id, req.Name, req.Description, req.Code), ct);
                return Results.Ok(dto);
            }
            catch (CodedValueNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        group.MapDelete("/{id:guid}/override", async (
            Guid id,
            [FromServices] ICommandHandler<RemoveCodedValueOverride> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(new RemoveCodedValueOverride(id), ct);
            return Results.NoContent();
        });

        // ── Provisional coded-value approval (tcv/3) ───────────────────────────────
        // A tenant creates a provisional (tenant-owned) value when an override is
        // impossible (e.g. Code AND Description both change). These rows stay hidden
        // from other tenants until a system-wide Settings-admin approval promotes them
        // to the shared blueprint, or a rejection keeps them tenant-scoped.
        group.MapPost("/provisional", async (
            [FromBody] CreateProvisionalCodedValue command,
            [FromServices] ICommandHandler<CreateProvisionalCodedValue, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/api/coded-values/provisional/{id}", new { id });
            }
            catch (CodedValueCodeConflictException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        group.MapGet("/provisional", async (
            [FromServices] IQueryHandler<ListProvisionalCodedValues, CodedValueDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListProvisionalCodedValues(), ct)));

        group.MapPost("/provisional/{id:guid}/approve", async (
            Guid id,
            [FromServices] ICommandHandler<ApproveProvisionalCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new ApproveProvisionalCodedValue(id), ct);
                return Results.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return Results.NotFound();
            }
            catch (CodedValueCodeConflictException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        group.MapPost("/provisional/{id:guid}/reject", async (
            Guid id,
            [FromServices] ICommandHandler<RejectProvisionalCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RejectProvisionalCodedValue(id), ct);
                return Results.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        return group;
    }
}

internal record UpdateCodedValueRequest(string Name, string? Description, int DisplayOrder);
internal record UpsertOverrideRequest(string? Name, string? Description, string? Code = null);
internal record BulkCreateCodedValuesRequest(Guid ParentId, List<BulkCreateChildRequest> Children);
internal record BulkCreateChildRequest(string Code, string Name, string? Description, int DisplayOrder);
