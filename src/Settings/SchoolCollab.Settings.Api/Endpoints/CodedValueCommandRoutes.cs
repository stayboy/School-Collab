using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.DeleteCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.DisableCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.EnableCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RecoverCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpdateCodedValue;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Api.Endpoints;

/// <summary>
/// Coded-value write routes: create, bulk create, update, soft delete and the enable/disable/recover
/// lifecycle. Split out of the former single <c>CodedValueRoutes</c> file by URI cluster
/// (spec <c>minimal-api-typed-responses</c> D9).
/// </summary>
public static class CodedValueCommandRoutes
{
    public static RouteGroupBuilder MapCodedValueCommandRoutes(this RouteGroupBuilder group)
    {
        group.MapPost("/", async Task<Results<Created<IdResponse>, Conflict<MessageResponse>>> (
            [FromBody] CreateCodedValue command,
            [FromServices] ICommandHandler<CreateCodedValue, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return TypedResults.Created($"/api/coded-values/{id}", new IdResponse(id));
            }
            catch (CodedValueCodeConflictException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
        });

        group.MapPost("/bulk",
            async Task<Results<Ok<BulkCreateResponse>, NotFound<MessageResponse>, Conflict<MessageResponse>>> (
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
                return TypedResults.Ok(
                    new BulkCreateResponse(result.ParentId, result.CreatedCount, result.SkippedCodes));
            }
            catch (CodedValueNotFoundException)
            {
                return TypedResults.NotFound(
                    new MessageResponse($"Parent coded value with ID '{req.ParentId}' not found."));
            }
            catch (DuplicateCodeException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
        });

        group.MapPut("/{id:guid}",
            async Task<Results<NoContent, NotFound, Conflict<MessageResponse>>> (
            Guid id,
            [FromBody] UpdateCodedValueRequest req,
            [FromServices] ICommandHandler<UpdateCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateCodedValue(id, req.Name, req.Description, req.DisplayOrder), ct);
                return TypedResults.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return TypedResults.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
        });

        group.MapDelete("/{id:guid}",
            async Task<Results<NoContent, NotFound, Conflict<MessageResponse>>> (
            Guid id,
            [FromServices] ICommandHandler<DeleteCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteCodedValue(id), ct);
                return TypedResults.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return TypedResults.NotFound();
            }
            catch (CodedValueHasChildrenException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
            catch (CodedValueReferencedException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
        });

        group.MapPost("/{id:guid}/recover", async Task<Results<NoContent, NotFound>> (
            Guid id,
            [FromServices] ICommandHandler<RecoverCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RecoverCodedValue(id), ct);
                return TypedResults.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return TypedResults.NotFound();
            }
        });

        group.MapPost("/{id:guid}/disable", async Task<Results<NoContent, NotFound>> (
            Guid id,
            [FromServices] ICommandHandler<DisableCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DisableCodedValue(id), ct);
                return TypedResults.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return TypedResults.NotFound();
            }
        });

        group.MapPost("/{id:guid}/enable", async Task<Results<NoContent, NotFound>> (
            Guid id,
            [FromServices] ICommandHandler<EnableCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new EnableCodedValue(id), ct);
                return TypedResults.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return TypedResults.NotFound();
            }
        });

        return group;
    }
}

internal record UpdateCodedValueRequest(string Name, string? Description, int DisplayOrder);
internal record BulkCreateCodedValuesRequest(Guid ParentId, List<BulkCreateChildRequest> Children);
internal record BulkCreateChildRequest(string Code, string Name, string? Description, int DisplayOrder);
