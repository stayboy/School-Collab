# API Endpoint Template

This reference shows all minimal API endpoint patterns used in School-Collab
bounded contexts.

## Program.cs Skeleton

```csharp
using SchoolCollab.{Context}.Core;
using SchoolCollab.{Context}.Core.CQRS;
using SchoolCollab.{Context}.Core.DTOs;
using SchoolCollab.{Context}.Core.Domain.Exceptions;
using SchoolCollab.{Context}.Core.Queries;
using SchoolCollab.{Context}.Core.Commands;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                    // MUST be first
builder.AddRabbitMQClient("rabbitmq");

var cacheConnectionString = builder.Configuration.GetConnectionString("cache")
    ?? builder.Configuration["Aspire:StackExchange:Redis:ConnectionString"];

if (string.IsNullOrWhiteSpace(cacheConnectionString))
    builder.Services.AddDistributedMemoryCache();
else
    builder.AddRedisDistributedCache("cache");

builder.Services.Add{Context}Core(builder.Configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapDefaultEndpoints();
app.UseSerilogRequestLogging();  // between MapDefaultEndpoints and endpoints

// --- Endpoints ---

// List all
app.MapGet("/{route}", async (
    [FromServices] IQueryHandler<List{Entities}, {Entity}Dto[]> handler,
    CancellationToken ct) =>
{
    var results = await handler.HandleAsync(new List{Entities}(), ct);
    return Results.Ok(results);
});

// Get by ID (404-safe)
app.MapGet("/{route}/{id:guid}", async (
    Guid id,
    [FromServices] IQueryHandler<Get{Entity}ById, {Entity}Dto?> handler,
    CancellationToken ct) =>
{
    var result = await handler.HandleAsync(new Get{Entity}ById(id), ct);
    return result is not null ? Results.Ok(result) : Results.NotFound();
});

// Create
app.MapPost("/{route}", async (
    Create{Entity}Request req,
    [FromServices] ICommandHandler<Create{Entity}, Guid> handler,
    CancellationToken ct) =>
{
    try
    {
        var id = await handler.HandleAsync(new Create{Entity}(req.Name, ...), ct);
        return Results.Created($"/{route}/{id}", id);
    }
    catch (DuplicateCodeException ex)
    {
        return Results.Conflict(ex.Message);
    }
});

// Update
app.MapPut("/{route}/{id:guid}", async (
    Guid id,
    Update{Entity}Request req,
    [FromServices] ICommandHandler<Update{Entity}> handler,
    CancellationToken ct) =>
{
    try
    {
        await handler.HandleAsync(new Update{Entity}(id, req.Name, ...), ct);
        return Results.NoContent();
    }
    catch ({Entity}NotFoundException)
    {
        return Results.NotFound();
    }
    catch (ConcurrencyException)
    {
        return Results.Conflict("The entity was modified by another user.");
    }
});

// Disable / Enable
app.MapPost("/{route}/{id:guid}/disable", async (
    Guid id,
    [FromServices] ICommandHandler<Disable{Entity}> handler,
    CancellationToken ct) =>
{
    try
    {
        await handler.HandleAsync(new Disable{Entity}(id), ct);
        return Results.NoContent();
    }
    catch ({Entity}NotFoundException)
    {
        return Results.NotFound();
    }
});

app.MapPost("/{route}/{id:guid}/enable", async (
    Guid id,
    [FromServices] ICommandHandler<Enable{Entity}> handler,
    CancellationToken ct) =>
{
    try
    {
        await handler.HandleAsync(new Enable{Entity}(id), ct);
        return Results.NoContent();
    }
    catch ({Entity}NotFoundException)
    {
        return Results.NotFound();
    }
});

app.Run();

// --- Request Records (internal) ---
internal record Create{Entity}Request(string Name, ...);
internal record Update{Entity}Request(string Name, ...);
```

## Endpoint Patterns

### List Endpoint
- Returns `Results.Ok(results)` with DTO array
- No caching at endpoint level (HybridCache is in the handler/repo layer)

### Get By ID Endpoint
- Returns `Results.Ok(result)` if found, `Results.NotFound()` if null
- Handler returns `null` for not-found (not exception)

### Create Endpoint
- Returns `Results.Created(location, id)` on success
- Catches domain exceptions: `DuplicateCodeException` → `Conflict`,
  validation exceptions → `BadRequest`

### Update Endpoint
- Returns `Results.NoContent()` on success
- Catches `NotFoundException` → `NotFound`, `ConcurrencyException` → `Conflict`

### Disable/Enable Endpoints
- Use `POST` (not `PATCH`) for state transitions
- Return `Results.NoContent()` on success
- Catch `NotFoundException` → `NotFound`

## Error Handling Patterns

### Domain Exceptions

```csharp
// In Core/Domain/Exceptions/
public sealed class {Entity}NotFoundException : DomainException
{
    public {Entity}NotFoundException(Guid id)
        : base($"Entity with ID '{id}' was not found.") { }
}

public sealed class DuplicateCodeException : DomainException
{
    public DuplicateCodeException(string code)
        : base($"Entity with code '{code}' already exists.") { }
}

public sealed class ConcurrencyException : DomainException
{
    public ConcurrencyException()
        : base("The entity was modified by another user. Please refresh and try again.") { }
}
```

### Base DomainException

```csharp
public abstract class DomainException(string message) : Exception(message);
```

## CQRS Handler Patterns

### Command Handler (no return)

```csharp
internal sealed class Disable{Entity}Handler(
    {Context}DbContext db,
    ILogger<Disable{Entity}Handler> logger) : ICommandHandler<Disable{Entity}>
{
    public async Task HandleAsync(Disable{Entity} cmd, CancellationToken ct)
    {
        logger.LogDebug("Disabling {Entity} {Id}", nameof({Entity}), cmd.Id);
        var entity = await db.{Entities}.FindAsync([cmd.Id], ct)
            ?? throw new {Entity}NotFoundException(cmd.Id);
        entity.Disable();
        await db.SaveChangesAsync(ct);
        logger.LogInformation("{Entity} {Id} disabled", nameof({Entity}), cmd.Id);
    }
}
```

### Command Handler (with return)

```csharp
internal sealed class Create{Entity}Handler(
    {Context}DbContext db,
    ILogger<Create{Entity}Handler> logger) : ICommandHandler<Create{Entity}, Guid>
{
    public async Task<Guid> HandleAsync(Create{Entity} cmd, CancellationToken ct)
    {
        logger.LogDebug("Creating {Entity} {Name}", nameof({Entity}), cmd.Name);
        var entity = {Entity}.Create(cmd.Name, ...);
        db.{Entities}.Add(entity);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("{Entity} {Id} created", nameof({Entity}), entity.Id);
        return entity.Id;
    }
}
```

### Query Handler

```csharp
internal sealed class List{Entities}Handler(
    I{Entity}Repository repository,
    ILogger<List{Entities}Handler> logger) : IQueryHandler<List{Entities}, {Entity}Dto[]>
{
    public async Task<{Entity}Dto[]> HandleAsync(List{Entities} query, CancellationToken ct)
    {
        logger.LogDebug("Listing {Entities}", nameof({Entity}));
        var results = await repository.ListAsync(ct);
        logger.LogDebug("Listed {Count} {Entities}", results.Length, nameof({Entity}));
        return results;
    }
}
```

### Query Handler (with cache)

```csharp
internal sealed class Get{Entity}ByIdHandler(
    I{Entity}Repository repository,
    HybridCache cache,
    ILogger<Get{Entity}ByIdHandler> logger) : IQueryHandler<Get{Entity}ById, {Entity}Dto?>
{
    public async Task<{Entity}Dto?> HandleAsync(Get{Entity}ById query, CancellationToken ct)
    {
        logger.LogDebug("Getting {Entity} {Id}", nameof({Entity}), query.Id);
        var result = await cache.GetOrCreateAsync(
            $"entities:{query.Id}",
            static async (_, ct) => await repository.GetByIdAsync(query.Id, ct),
            cancellationToken: ct);
        return result;
    }
}
```

## Structured Logging in Handlers

| Level | Pattern |
|---|---|
| Debug | `LogDebug("Handling {Action} {Id}", nameof(Create{Entity}), cmd.Id)` |
| Debug | `LogDebug("Listing {Entities}", nameof({Entity}))` |
| Info | `LogInformation("{Entity} {Id} created", nameof({Entity}), entity.Id)` |
| Info | `LogInformation("{Entity} {Id} disabled", nameof({Entity}), cmd.Id)` |
| Error | `LogError(ex, "Failed to create {Entity}", nameof({Entity}))` |