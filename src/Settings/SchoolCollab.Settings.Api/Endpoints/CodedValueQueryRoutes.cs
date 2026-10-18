using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueByCode;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueById;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValuesByIds;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValuesByParent;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.ListRootCodedValues;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.SearchCodedValues;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

/// <summary>
/// Coded-value read routes: text search, root listing, lookup by id / code / ids / parent, and the
/// soft-deleted listing. Split out of the former single <c>CodedValueRoutes</c> file by URI cluster
/// (spec <c>minimal-api-typed-responses</c> D9).
/// </summary>
public static class CodedValueQueryRoutes
{
    public static RouteGroupBuilder MapCodedValueQueryRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/search", async (
            [FromQuery] string text,
            [FromQuery] Guid? parentId,
            [FromQuery] bool? includeDisabled,
            [FromServices] IQueryHandler<SearchCodedValues, CodedValueDto[]> handler,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(text))
                return TypedResults.Ok(Array.Empty<CodedValueDto>());

            var result = await handler.HandleAsync(
                new SearchCodedValues(text, parentId, includeDisabled ?? false), ct);
            return TypedResults.Ok(result);
        });

        group.MapGet("/", async (
            [FromServices] IQueryHandler<ListRootCodedValues, CodedValueDto[]> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListRootCodedValues(), ct)));

        group.MapGet("/{id:guid}", async Task<Results<Ok<CodedValueDto>, NotFound>> (
            Guid id,
            [FromServices] IQueryHandler<GetCodedValueById, CodedValueDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCodedValueById(id), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        });

        group.MapGet("/by-code/{code}", async Task<Results<Ok<CodedValueDto>, NotFound>> (
            string code,
            [FromQuery] Guid? parentId,
            [FromServices] IQueryHandler<GetCodedValueByCode, CodedValueDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCodedValueByCode(code, parentId), ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        });

        group.MapGet("/by-ids", async (
            [FromQuery] Guid[] ids,
            [FromServices] IQueryHandler<GetCodedValuesByIds, CodedValueDto[]> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetCodedValuesByIds(ids), ct)));

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

            return TypedResults.Ok(await handler.HandleAsync(
                new GetCodedValuesByParent(parentId, parentCode, filters, includeDisabled ?? false), ct));
        });

        group.MapGet("/deleted", async (
            [FromServices] ICodedValueRepository repository,
            CancellationToken ct) =>
            TypedResults.Ok(await repository.ListDeletedAsync(ct)));

        return group;
    }
}
