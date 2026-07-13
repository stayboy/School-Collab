using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.AddContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.DeleteContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.SetPrimaryContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.UpdateContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.VerifyContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Queries.GetSubscription;
using SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListContacts;
using SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListSubscribedContacts;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Api.Endpoints;

public static class ContactRoutes
{
    public static RouteGroupBuilder MapContactRoutes(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            [FromBody] AddContact command,
            [FromServices] ICommandHandler<AddContact, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/contacts/{id}", new { id });
        });

        group.MapGet("/", async (
            ContactOwnerType ownerType,
            Guid ownerId,
            [FromServices] IQueryHandler<ListContacts, ContactDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListContacts(ownerType, ownerId), ct)));

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateContactRequest req,
            [FromServices] ICommandHandler<UpdateContact> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateContact(id, req.Value, req.Label), ct);
                return Results.NoContent();
            }
            catch (ContactNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] ICommandHandler<DeleteContact> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteContact(id), ct);
                return Results.NoContent();
            }
            catch (ContactNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPost("/{id:guid}/verify", async (
            Guid id,
            [FromServices] ICommandHandler<VerifyContact> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new VerifyContact(id), ct);
                return Results.NoContent();
            }
            catch (ContactNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPost("/{id:guid}/set-primary", async (
            Guid id,
            [FromServices] ICommandHandler<SetPrimaryContact> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SetPrimaryContact(id), ct);
                return Results.NoContent();
            }
            catch (ContactNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        // Cross-BC resolver contract (spec §9 G5).
        group.MapGet("/subscribed", async (
            ContactOwnerType ownerType,
            Guid ownerId,
            SubscriptionScope? scope,
            [FromServices] IQueryHandler<ListSubscribedContacts, SubscribedContactDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListSubscribedContacts(ownerType, ownerId, scope), ct)));

        group.MapGet("/{id:guid}/subscription", async (
            Guid id,
            SubscriptionScope scope,
            Guid? scopeRefId,
            [FromServices] IQueryHandler<GetSubscription, ContactSubscriptionDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetSubscription(id, scope, scopeRefId), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return group;
    }
}

internal record UpdateContactRequest(string Value, string? Label);
