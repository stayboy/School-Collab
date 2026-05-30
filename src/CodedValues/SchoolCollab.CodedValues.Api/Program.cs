using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core;
using SchoolCollab.CodedValues.Core.Commands.CreateCodedValue;
using SchoolCollab.CodedValues.Core.Commands.DisableCodedValue;
using SchoolCollab.CodedValues.Core.Commands.EnableCodedValue;
using SchoolCollab.CodedValues.Core.Commands.RemoveCodedValueAttribute;
using SchoolCollab.CodedValues.Core.Commands.SetCodedValueAttribute;
using SchoolCollab.CodedValues.Core.Commands.UpdateCodedValue;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;
using SchoolCollab.CodedValues.Core.DTOs;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValueById;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValuesByIds;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValuesByParent;
using SchoolCollab.CodedValues.Core.Queries.ListRootCodedValues;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddCodedValuesCore(builder.Configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CodedValuesDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

app.MapGet("/coded-values", async (
    [FromServices] IQueryHandler<ListRootCodedValues, CodedValueDto[]> handler,
    CancellationToken ct) =>
    Results.Ok(await handler.HandleAsync(new ListRootCodedValues(), ct)));

app.MapGet("/coded-values/{id:guid}", async (
    Guid id,
    [FromServices] IQueryHandler<GetCodedValueById, CodedValueDto> handler,
    CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await handler.HandleAsync(new GetCodedValueById(id), ct));
    }
    catch (CodedValueNotFoundException)
    {
        return Results.NotFound();
    }
});

app.MapGet("/coded-values/by-ids", async (
    [FromQuery] Guid[] ids,
    [FromServices] IQueryHandler<GetCodedValuesByIds, CodedValueDto[]> handler,
    CancellationToken ct) =>
    Results.Ok(await handler.HandleAsync(new GetCodedValuesByIds(ids), ct)));

app.MapGet("/coded-values/by-parent", async (
    [FromQuery] Guid? parentId,
    [FromQuery] string? parentCode,
    [FromQuery] string? attributeKey,
    [FromQuery] string? attributeValue,
    [FromQuery] bool includeDisabled,
    [FromServices] IQueryHandler<GetCodedValuesByParent, CodedValueDto[]> handler,
    CancellationToken ct) =>
{
    IReadOnlyDictionary<string, string>? filters = null;
    if (!string.IsNullOrWhiteSpace(attributeKey) && !string.IsNullOrWhiteSpace(attributeValue))
    {
        filters = new Dictionary<string, string> { [attributeKey] = attributeValue };
    }

    return Results.Ok(await handler.HandleAsync(
        new GetCodedValuesByParent(parentId, parentCode, filters, includeDisabled), ct));
});

app.MapPost("/coded-values", async (
    [FromBody] CreateCodedValue command,
    [FromServices] ICommandHandler<CreateCodedValue> handler,
    CancellationToken ct) =>
{
    try
    {
        await handler.HandleAsync(command, ct);
        return Results.Created("/coded-values", null);
    }
    catch (DuplicateCodeException ex)
    {
        return Results.Conflict(new { ex.Message });
    }
});

app.MapPut("/coded-values/{id:guid}", async (
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

app.MapPost("/coded-values/{id:guid}/disable", async (
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

app.MapPost("/coded-values/{id:guid}/enable", async (
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

app.MapPut("/coded-values/{id:guid}/attributes/{key}", async (
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

app.MapDelete("/coded-values/{id:guid}/attributes/{key}", async (
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

app.Run();

internal record UpdateCodedValueRequest(string Name, string? Description, int DisplayOrder);
internal record AttributeValueRequest(string Value);

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }
