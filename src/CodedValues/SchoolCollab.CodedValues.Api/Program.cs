using Microsoft.AspNetCore.Mvc;
using Serilog;
using SchoolCollab.CodedValues.Core;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Commands.CreateCodedValue;
using SchoolCollab.CodedValues.Core.Commands.DisableCodedValue;
using SchoolCollab.CodedValues.Core.Commands.EnableCodedValue;
using SchoolCollab.CodedValues.Core.Commands.DeleteCodedValue;
using SchoolCollab.CodedValues.Core.Commands.RecoverCodedValue;
using SchoolCollab.CodedValues.Core.Commands.RemoveCodedValueAttribute;
using SchoolCollab.CodedValues.Core.Commands.SetCodedValueAttribute;
using SchoolCollab.CodedValues.Core.Commands.RemoveCodedValueAttributeDefinition;
using SchoolCollab.CodedValues.Core.Commands.SetCodedValueAttributeDefinition;
using SchoolCollab.CodedValues.Core.Commands.UpdateCodedValue;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;
using SchoolCollab.CodedValues.Core.DTOs;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValueById;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValueByCode;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValuesByIds;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValuesByParent;
using SchoolCollab.CodedValues.Core.Queries.SearchCodedValues;
using SchoolCollab.CodedValues.Core.Queries.ListRootCodedValues;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.CodedValues.Api.Infrastructure.Auth;
using SchoolCollab.CodedValues.Api.Infrastructure.Data;
using SchoolCollab.Core.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("rabbitmq");

var cacheConnectionString = builder.Configuration.GetConnectionString("cache")
    ?? builder.Configuration["Aspire:StackExchange:Redis:ConnectionString"];

if (string.IsNullOrWhiteSpace(cacheConnectionString))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.AddRedisDistributedCache("cache");
}

builder.Services.AddCodedValuesCore(builder.Configuration);
builder.Services.AddOpenApi();

// Tenancy & Auth Infrastructure
builder.Services.AddAuthAndTenancy(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.UseSerilogRequestLogging();

var codedValuesGroup = app.MapGroup("/coded-values").RequireAuthorization();

codedValuesGroup.MapGet("/search", async (
    [FromQuery] string text,
    [FromQuery] Guid? parentId,
    [FromQuery] bool? includeDisabled,
    [FromServices] IQueryHandler<SearchCodedValues, CodedValueDto[]> handler,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(text))
        return Results.Ok(Array.Empty<CodedValueDto>());

    var result = await handler.HandleAsync(
        new SearchCodedValues(text, parentId, includeDisabled ?? false), ct);
    return Results.Ok(result);
});

codedValuesGroup.MapGet("/", async (
    [FromServices] IQueryHandler<ListRootCodedValues, CodedValueDto[]> handler,
    CancellationToken ct) =>
    Results.Ok(await handler.HandleAsync(new ListRootCodedValues(), ct)));

codedValuesGroup.MapGet("/{id:guid}", async (
    Guid id,
    [FromServices] IQueryHandler<GetCodedValueById, CodedValueDto?> handler,
    CancellationToken ct) =>
{
    var result = await handler.HandleAsync(new GetCodedValueById(id), ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

codedValuesGroup.MapGet("/by-code/{code}", async (
    string code,
    [FromQuery] Guid? parentId,
    [FromServices] IQueryHandler<GetCodedValueByCode, CodedValueDto?> handler,
    CancellationToken ct) =>
{
    var result = await handler.HandleAsync(new GetCodedValueByCode(code, parentId), ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

codedValuesGroup.MapGet("/by-ids", async (
    [FromQuery] Guid[] ids,
    [FromServices] IQueryHandler<GetCodedValuesByIds, CodedValueDto[]> handler,
    CancellationToken ct) =>
    Results.Ok(await handler.HandleAsync(new GetCodedValuesByIds(ids), ct)));

codedValuesGroup.MapGet("/by-parent", async (
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

    return Results.Ok(await handler.HandleAsync(
        new GetCodedValuesByParent(parentId, parentCode, filters, includeDisabled ?? false), ct));
});

codedValuesGroup.MapDelete("/{id:guid}", async (
    Guid id,
    [FromServices] ICommandHandler<DeleteCodedValue> handler,
    CancellationToken ct) =>
{
    try
    {
        await handler.HandleAsync(new DeleteCodedValue(id), ct);
        return Results.NoContent();
    }
    catch (CodedValueNotFoundException)
    {
        return Results.NotFound();
    }
    catch (CodedValueHasChildrenException ex)
    {
        return Results.Conflict(new { ex.Message });
    }
    catch (CodedValueReferencedException ex)
    {
        return Results.Conflict(new { ex.Message });
    }
});

codedValuesGroup.MapPost("/{id:guid}/recover", async (
    Guid id,
    [FromServices] ICommandHandler<RecoverCodedValue> handler,
    CancellationToken ct) =>
{
    try
    {
        await handler.HandleAsync(new RecoverCodedValue(id), ct);
        return Results.NoContent();
    }
    catch (CodedValueNotFoundException)
    {
        return Results.NotFound();
    }
});

codedValuesGroup.MapGet("/deleted", async (
    [FromServices] ICodedValueRepository repository,
    CancellationToken ct) =>
    Results.Ok(await repository.ListDeletedAsync(ct)));

codedValuesGroup.MapPost("/", async (
    [FromBody] CreateCodedValue command,
    [FromServices] ICommandHandler<CreateCodedValue, Guid> handler,
    CancellationToken ct) =>
{
    try
    {
        var id = await handler.HandleAsync(command, ct);
        return Results.Created($"/coded-values/{id}", new { id });
    }
    catch (DuplicateCodeException ex)
    {
        return Results.Conflict(new { ex.Message });
    }
});

codedValuesGroup.MapPost("/bulk", async (
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
        return Results.Ok(new { result.ParentId, result.CreatedCount, result.SkippedCodes });
    }
    catch (CodedValueNotFoundException)
    {
        return Results.NotFound(new { Message = $"Parent coded value with ID '{req.ParentId}' not found." });
    }
    catch (DuplicateCodeException ex)
    {
        // Only thrown for intra-batch duplicates
        return Results.Conflict(new { ex.Message });
    }
});

codedValuesGroup.MapPut("/{id:guid}", async (
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

codedValuesGroup.MapPost("/{id:guid}/disable", async (
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

codedValuesGroup.MapPost("/{id:guid}/enable", async (
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

codedValuesGroup.MapPut("/{id:guid}/attributes/{key}", async (
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

codedValuesGroup.MapDelete("/{id:guid}/attributes/{key}", async (
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

codedValuesGroup.MapPut("/{id:guid}/attribute-definitions/{key}", async (
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

codedValuesGroup.MapDelete("/{id:guid}/attribute-definitions/{key}", async (
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

app.Run();

internal record UpdateCodedValueRequest(string Name, string? Description, int DisplayOrder);
internal record AttributeValueRequest(string Value);
internal record AttributeDefinitionRequest(string? DisplayName, AttributeDataType DataType, string? SourceCode, bool IsRequired, bool AllowMultiple = false, int? MinLength = null, int? MaxLength = null, string? RegexPattern = null);
internal record BulkCreateCodedValuesRequest(Guid ParentId, List<BulkCreateChildRequest> Children);
internal record BulkCreateChildRequest(string Code, string Name, string? Description, int DisplayOrder);

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }
