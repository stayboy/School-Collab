using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueAttributeDefinition;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.SetCodedValueAttributeDefinition;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class CodedValueAttributeDefinitionRoutes
{
    public static RouteGroupBuilder MapCodedValueAttributeDefinitionRoutes(this RouteGroupBuilder group)
    {
        // ── Coded Value Attribute Definitions (per-key schema set/remove) ────────

        group.MapPut("/{id:guid}/attribute-definitions/{key}",
            async Task<Results<NoContent, NotFound, Conflict<MessageResponse>>> (
            Guid id,
            string key,
            [FromBody] AttributeDefinitionRequest req,
            [FromServices] ICommandHandler<SetCodedValueAttributeDefinition> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SetCodedValueAttributeDefinition(id, key, req.DisplayName, req.DataType, req.SourceCode, req.IsRequired, req.AllowMultiple, req.MinLength, req.MaxLength, req.RegexPattern), ct);
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

        group.MapDelete("/{id:guid}/attribute-definitions/{key}",
            async Task<Results<NoContent, NotFound, Conflict<MessageResponse>>> (
            Guid id,
            string key,
            [FromServices] ICommandHandler<RemoveCodedValueAttributeDefinition> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveCodedValueAttributeDefinition(id, key), ct);
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

        return group;
    }
}

internal record AttributeDefinitionRequest(string? DisplayName, AttributeDataType DataType, string? SourceCode, bool IsRequired, bool AllowMultiple = false, int? MinLength = null, int? MaxLength = null, string? RegexPattern = null);
