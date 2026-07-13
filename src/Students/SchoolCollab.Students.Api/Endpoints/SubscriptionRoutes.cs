using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.Subscribe;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.Unsubscribe;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Api.Endpoints;

public static class SubscriptionRoutes
{
    public static RouteGroupBuilder MapSubscriptionRoutes(this RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/subscribe", async (
            Guid id,
            [FromBody] SubscriptionRequest req,
            [FromServices] ICommandHandler<Subscribe> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new Subscribe(id, req.Scope, req.ScopeRefId), ct);
                return Results.NoContent();
            }
            catch (ContactNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/{id:guid}/unsubscribe", async (
            Guid id,
            [FromBody] SubscriptionRequest req,
            [FromServices] ICommandHandler<Unsubscribe> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new Unsubscribe(id, req.Scope, req.ScopeRefId), ct);
                return Results.NoContent();
            }
            catch (ContactNotFoundException)
            {
                return Results.NotFound();
            }
        });

        return group;
    }
}

internal record SubscriptionRequest(SubscriptionScope Scope, Guid? ScopeRefId);
