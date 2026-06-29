using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.RemoveCodedValueAttributeDefinition;
using SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.SetCodedValueAttributeDefinition;
using SchoolCollab.Core.CQRS;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Api.Endpoints;

public static class CodedValueAttributeDefinitionRoutes
{
    public static RouteGroupBuilder MapCodedValueAttributeDefinitionRoutes(this RouteGroupBuilder group)
    {
        // ── Coded Value Attribute Definitions (per-key schema set/remove) ────────

        group.MapPut("/{id:guid}/attribute-definitions/{key}", async (
            Guid id,
            string key,
            [FromBody] AttributeDefinitionRequest req,
            [FromServices] ICommandHandler<SetCodedValueAttributeDefinition> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SetCodedValueAttributeDefinition(id, key, req.DisplayName, req.DataType, req.SourceCode, req.IsRequired, req.AllowMultiple, req.MinLength, req.MaxLength, req.RegexPattern), ct);
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

        group.MapDelete("/{id:guid}/attribute-definitions/{key}", async (
            Guid id,
            string key,
            [FromServices] ICommandHandler<RemoveCodedValueAttributeDefinition> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveCodedValueAttributeDefinition(id, key), ct);
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

        return group;
    }
}

internal record AttributeDefinitionRequest(string? DisplayName, AttributeDataType DataType, string? SourceCode, bool IsRequired, bool AllowMultiple = false, int? MinLength = null, int? MaxLength = null, string? RegexPattern = null);
