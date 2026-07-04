using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueAttribute;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.SetCodedValueAttribute;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class CodedValueAttributeRoutes
{
    public static RouteGroupBuilder MapCodedValueAttributeRoutes(this RouteGroupBuilder group)
    {
        // ── Coded Value Attributes (per-key value set/remove) ───────────────────

        group.MapPut("/{id:guid}/attributes/{key}", async (
            Guid id,
            string key,
            [FromBody] AttributeValueRequest req,
            [FromServices] ICommandHandler<SetCodedValueAttribute> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SetCodedValueAttribute(id, key, req.Value), ct);
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

        group.MapDelete("/{id:guid}/attributes/{key}", async (
            Guid id,
            string key,
            [FromServices] ICommandHandler<RemoveCodedValueAttribute> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveCodedValueAttribute(id, key), ct);
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

internal record AttributeValueRequest(string Value);
