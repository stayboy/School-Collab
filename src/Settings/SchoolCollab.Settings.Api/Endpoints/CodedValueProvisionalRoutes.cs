using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.ApproveProvisionalCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateProvisionalCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RejectProvisionalCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.ListProvisionalCodedValues;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

/// <summary>
/// Provisional coded-value routes: tenant creation, the admin approval queue, and approve/reject. Split
/// out of the former single <c>CodedValueRoutes</c> file by URI cluster
/// (spec <c>minimal-api-typed-responses</c> D9) — the query/mutation mix is why this cluster is cut by
/// URI rather than by CQRS.
/// </summary>
public static class CodedValueProvisionalRoutes
{
    public static RouteGroupBuilder MapCodedValueProvisionalRoutes(this RouteGroupBuilder group)
    {
        // A tenant creates a provisional (tenant-owned) value when an override is
        // impossible (e.g. Code AND Description both change). These rows stay hidden
        // from other tenants until a system-wide Settings-admin approval promotes them
        // to the shared blueprint, or a rejection keeps them tenant-scoped.
        group.MapPost("/provisional",
            async Task<Results<Created<IdResponse>, Conflict<MessageResponse>, BadRequest<MessageResponse>>> (
            [FromBody] CreateProvisionalCodedValue command,
            [FromServices] ICommandHandler<CreateProvisionalCodedValue, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return TypedResults.Created($"/api/coded-values/provisional/{id}", new IdResponse(id));
            }
            catch (CodedValueCodeConflictException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest(new MessageResponse(ex.Message));
            }
        });

        group.MapGet("/provisional", async (
            [FromServices] IQueryHandler<ListProvisionalCodedValues, CodedValueDto[]> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListProvisionalCodedValues(), ct)));

        // Deliberately untyped: four outcome arms exceed the three-arm ceiling that keeps a
        // Results<…> union readable, and no .Produces call is added for a body the wire contract
        // does not carry (spec minimal-api-typed-responses D11).
        group.MapPost("/provisional/{id:guid}/approve", async Task<IResult> (
            Guid id,
            [FromServices] ICommandHandler<ApproveProvisionalCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new ApproveProvisionalCodedValue(id), ct);
                return TypedResults.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return TypedResults.NotFound();
            }
            catch (CodedValueCodeConflictException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest(new MessageResponse(ex.Message));
            }
        });

        group.MapPost("/provisional/{id:guid}/reject",
            async Task<Results<NoContent, BadRequest<MessageResponse>, NotFound>> (
            Guid id,
            [FromServices] ICommandHandler<RejectProvisionalCodedValue> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RejectProvisionalCodedValue(id), ct);
                return TypedResults.NoContent();
            }
            catch (CodedValueNotFoundException)
            {
                return TypedResults.NotFound();
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest(new MessageResponse(ex.Message));
            }
        });

        return group;
    }
}
