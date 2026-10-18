using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueOverride;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpsertCodedValueOverride;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

/// <summary>
/// Tenant coded-value override routes. Split out of the former single <c>CodedValueRoutes</c> file by
/// URI cluster (spec <c>minimal-api-typed-responses</c> D9).
/// </summary>
public static class CodedValueOverrideRoutes
{
    public static RouteGroupBuilder MapCodedValueOverrideRoutes(this RouteGroupBuilder group)
    {
        // PUT creates/updates the override; DELETE removes it (idempotent). The
        // handler resolves the current tenant from ITenantProvider (not the URL),
        // so these are the per-tenant display-name overrides consumed by the
        // wizard's override dialog (§6.2). See spec §5.1.
        group.MapPut("/{id:guid}/override",
            async Task<Results<Ok<CodedValueDto>, BadRequest<MessageResponse>, NotFound>> (
            Guid id,
            [FromBody] UpsertOverrideRequest req,
            [FromServices] ICommandHandler<UpsertCodedValueOverride, CodedValueDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var dto = await handler.HandleAsync(
                    new UpsertCodedValueOverride(id, req.Name, req.Description, req.Code), ct);
                return TypedResults.Ok(dto);
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

        group.MapDelete("/{id:guid}/override", async (
            Guid id,
            [FromServices] ICommandHandler<RemoveCodedValueOverride> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(new RemoveCodedValueOverride(id), ct);
            return TypedResults.NoContent();
        });

        return group;
    }
}

internal record UpsertOverrideRequest(string? Name, string? Description, string? Code = null);
