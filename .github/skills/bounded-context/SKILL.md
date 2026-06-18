---
name: bounded-context
description: |
  Build a new bounded context (backend + admin UI) in the School-Collab repo.
  Covers the 6-project structure, domain entities, CQRS handlers, minimal API,
  Blazor admin pages, Aspire wiring, EF migrations, and testing conventions.
  Triggers: "create bounded context", "new bounded context", "add module",
  "build API endpoint", "CQRS handler", "Blazor page pattern", "admin page",
  "new feature module", "bounded context template".
---

# Bounded Context Builder

Create a new bounded context following the School-Collab architecture. Each
context is a self-contained vertical slice with its own database, API, admin
UI, and optional background worker.

## Project Structure

Every bounded context follows this 6-project layout under `src/{Context}/`:

```
src/{Context}/
├── SchoolCollab.{Context}.Core/        # Domain + Data + CQRS + Messaging
├── SchoolCollab.{Context}.Contracts/   # Integration events (MassTransit)
├── SchoolCollab.{Context}.Api/         # Minimal API
├── SchoolCollab.{Context}.Admin/        # Blazor SSR admin pages
├── SchoolCollab.{Context}.Worker/      # Background services (optional)
└── SchoolCollab.{Context}.Tests.Unit/  # Unit tests
```

**No direct project references between contexts.** Inter-context communication
uses MassTransit integration events defined in `Contracts`.

## Phase 1: Core Domain & Data

### 1.1 Create Core Project

Create `SchoolCollab.{Context}.Core` with this folder structure:

```
Core/
├── CQRS/
│   ├── ICommand.cs
│   ├── ICommandHandler.cs
│   ├── IQuery.cs
│   └── IQueryHandler.cs
├── Commands/{Action}/              # One folder per command
├── Queries/{Action}/              # One folder per query
├── DTOs/                          # Data transfer objects
├── Domain/
│   ├── {Entity}.cs                # Aggregate roots
│   ├── Events/                    # Domain events (IDomainEvent)
│   ├── Exceptions/                # DomainException inheritors
│   └── Enums/                     # Value enums
├── Data/
│   ├── {Context}DbContext.cs
│   ├── DesignTime{Context}DbContextFactory.cs
│   ├── Configurations/            # IEntityTypeConfiguration<T>
│   ├── Migrations/
│   └── Repositories/
├── Caching/                       # HybridCache + CacheKeyHelper
├── Messaging/
│   ├── IIntegrationEventPublisher.cs
│   ├── OutboxIntegrationEventPublisher.cs
│   └── OutboxDispatcher.cs
└── Extensions.cs                  # Add{Context}Core() DI method
```

### 1.2 CQRS Interfaces

Copy these four interfaces from CodedValues — they are simple marker + handler
contracts (no MediatR):

```csharp
// CQRS/ICommand.cs
namespace SchoolCollab.{Context}.Core.CQRS;
public interface ICommand { }

// CQRS/ICommandHandler.cs
namespace SchoolCollab.{Context}.Core.CQRS;
public interface ICommandHandler<TCommand> where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken ct = default);
}
public interface ICommandHandler<TCommand, TResult> where TCommand : ICommand
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default);
}

// CQRS/IQuery.cs
namespace SchoolCollab.{Context}.Core.CQRS;
public interface IQuery<TResult> where TResult : class? { }

// CQRS/IQueryHandler.cs
namespace SchoolCollab.{Context}.Core.CQRS;
public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult> where TResult : class?
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct = default);
}
```

Commands are `sealed record` types. Handlers use primary constructor DI:

```csharp
public sealed record CreateStudent(string FirstName, string LastName)
    : ICommand;

internal sealed class CreateStudentHandler(
    StudentsDbContext db,
    ILogger<CreateStudentHandler> logger) : ICommandHandler<CreateStudent, Guid>
{
    public async Task<Guid> HandleAsync(CreateStudent cmd, CancellationToken ct)
    {
        logger.LogDebug("Handling CreateStudent {Name}", cmd.FirstName);
        var entity = Student.Create(cmd.FirstName, cmd.LastName);
        db.Students.Add(entity);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Student {Id} created", entity.Id);
        return entity.Id;
    }
}
```

### 1.3 Domain Entity Pattern

Every aggregate root follows this pattern:

- **Private constructor** (EF Core) + **static `Create()` factory method**
- **`_domainEvents` list** exposed via `IReadOnlyCollection<IDomainEvent>`
- **`ClearDomainEvents()`** method for dispatching after save
- **PostgreSQL `xmin`** for optimistic concurrency (`[Timestamp] byte[] RowVersion`)
- **Private `List<T>` fields** exposed as `IReadOnlyCollection<T>`

```csharp
public sealed class Student
{
    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }
    [Timestamp] public byte[] RowVersion { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;
    public void ClearDomainEvents() => _domainEvents.Clear();

    private Student() { } // EF

    public static Student Create(string firstName, string lastName)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        student._domainEvents.Add(new StudentCreated(student.Id));
        return student;
    }
}
```

### 1.4 DbContext & Migrations

```csharp
// Data/{Context}DbContext.cs
public sealed class {Context}DbContext : DbContext
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public {Context}DbContext(DbContextOptions<{Context}DbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(
            typeof({Context}DbContext).Assembly);
}
```

```csharp
// Data/DesignTime{Context}DbContextFactory.cs
public sealed class DesignTime{Context}DbContextFactory
    : IDesignTimeDbContextFactory<{Context}DbContext>
{
    public {Context}DbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<{Context}DbContext>();
        optionsBuilder
            .UseNpgsql("Host=localhost;Port=5432;Database=schoolcollab_{context_snake};Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();
        return new {Context}DbContext(optionsBuilder.Options);
    }
}
```

EF configurations use `IEntityTypeConfiguration<T>` with snake_case tables
and `xmin` RowVersion. Generate migration from repo root:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/{Context}/SchoolCollab.{Context}.Core \
  --context {Context}DbContext
```

### 1.5 Extensions.cs — DI Registration

Every Core project exposes a single `Add{Context}Core()` extension:

```csharp
public static class Extensions
{
    public static IServiceCollection Add{Context}Core(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("{context-dash}-db")
            ?? configuration["ConnectionStrings:{context-dash}-db"]
            ?? "Host=localhost;...";

        services.AddDbContext<{Context}DbContext>(opts =>
            opts.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        services.AddScoped<IStudentRepository, StudentRepository>();

        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            };
        });

        var assembly = typeof(Extensions).Assembly;
        // Scrutor scans for ICommandHandler<>, ICommandHandler<,>, IQueryHandler<,>
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
            .AsImplementedInterfaces().WithTransientLifetime());
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces().WithTransientLifetime());
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces().WithTransientLifetime());

        services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}
```

### 1.6 Outbox Pattern

The outbox ensures at-least-once delivery of integration events:

- **`IIntegrationEventPublisher`** — enqueues events into the same DB transaction
  (does NOT call `SaveChanges` — same unit of work).
- **`OutboxIntegrationEventPublisher`** — serializes to JSON, adds `OutboxMessage` row.
- **`OutboxDispatcher`** — `BackgroundService` that polls with
  `FOR UPDATE SKIP LOCKED`, publishes to RabbitMQ, marks dispatched.

## Phase 2: API

### 2.1 Program.cs Pattern

```csharp
using SchoolCollab.{Context}.Core;
using SchoolCollab.{Context}.Core.CQRS;
// ... command/query using directives

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

// Minimal API endpoints inject handlers directly
app.MapGet("/{context-route}", async (
    [FromServices] IQueryHandler<List{Entities}, {Entity}Dto[]> handler,
    CancellationToken ct) =>
    Results.Ok(await handler.HandleAsync(new List{Entities}(), ct)));

app.Run();

// Request records at bottom (internal)
internal record Create{Entity}Request(string Name, ...);
```

### 2.2 Endpoint Conventions

- **Inject handlers directly** — no controller classes
- **Catch domain exceptions** — return `Results.NotFound()`, `Results.Conflict()`
- **404 pattern**: Use `GetAsync()` + status check + `ReadFromJsonAsync<T>()`
  instead of `GetFromJsonAsync<T>()` (which throws on 404)
- **Request records**: `internal record` at bottom of `Program.cs`
- **Never use `Console.WriteLine`** — always `ILogger<T>` with structured logging

## Phase 3: Admin UI

### 3.1 ApiClient Pattern

```csharp
public sealed class {Context}ApiClient(HttpClient http)
{
    // 404-safe pattern for get-by-id:
    public async Task<{Entity}Dto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/{route}/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<{Entity}Dto>(ct);
    }

    // Standard CRUD:
    public Task<{Entity}Dto[]?> ListAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<{Entity}Dto[]>("/{route}", ct);

    public async Task CreateAsync(Create{Entity}Request req, CancellationToken ct = default) =>
        (await http.PostAsJsonAsync("/{route}", req, ct)).EnsureSuccessStatusCode();
}
```

### 3.2 ModuleServices.cs

```csharp
public static class ModuleServices
{
    public static IServiceCollection Add{Context}Module(this IServiceCollection services)
    {
        services.AddHttpClient<{Context}ApiClient>(client =>
            client.BaseAddress = new Uri("https+http://{context-dash}-api"));
        return services;
    }
}
```

Register in `SchoolCollab.Admin/Program.cs`:
```csharp
services.Add{Context}Module();
```

### 3.3 Blazor Page Pattern (CRITICAL)

Every page **must** follow these rules. The canonical example is
`CodedValues/Admin/Components/Pages/CodedValues/Index.razor`.

#### Items-Null Loading (NOT _loading bool)
```razor
@if (_items is null)         { <FluentProgressRing /> }
else if (_items.Length == 0) { <FluentMessageBar>No items yet.</FluentMessageBar> }
else                         { <FluentDataGrid ... /> }
```

#### IDisposable + CancellationTokenSource
```csharp
@implements IDisposable
@code {
    private CancellationTokenSource? _loadCts;
    private volatile bool _disposed;

    protected override async Task OnInitializedAsync()
    {
        _loadCts = new CancellationTokenSource();
        try
        {
            _items = await Api.GetRootValuesAsync(_loadCts.Token);
            if (_disposed) return;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed) _error = ex.Message; }
    }

    public void Dispose()
    {
        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }
}
```

#### Optimistic Mutations (never re-fetch the whole list)
```csharp
private async Task OnToggleAsync(Guid id, bool disable)
{
    if (_items is null) return;
    var idx = Array.FindIndex(_items, x => x.Id == id);
    if (idx < 0) return;
    var previous = _items[idx];
    _items[idx] = previous with { IsDisabled = disable };
    StateHasChanged();
    try { await (disable ? Api.DisableAsync(id) : Api.EnableAsync(id)); }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Failed to toggle {Id}", id);
        var i = Array.FindIndex(_items, x => x.Id == id);
        if (i >= 0) _items[i] = previous;   // rollback
        _error = ex.Message;
        StateHasChanged();
    }
}
```

#### No Per-Page @rendermode
The render mode is set **once** in `App.razor`. Pages inherit it — never
declare `@rendermode` on individual pages.

#### FluentUI Only
Never mix Bootstrap elements. Use `FluentTextField`, `FluentButton`,
`FluentDataGrid`, `FluentMessageBar`, etc.

#### @key on Dynamic Lists
```razor
@foreach (var item in _items)
{
    <FluentCard @key="item.Id">...</FluentCard>
}
```

## Phase 4: Worker (Optional)

For background services (promotion, nightly jobs):

```csharp
// Worker/Program.cs
var builder = Host.CreateApplicationBuilder(args);  // NOT WebApplication

builder.AddServiceDefaults();
builder.AddRabbitMQClient("rabbitmq");
// ... conditional Redis cache ...
builder.Services.Add{Context}Core(builder.Configuration);
builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddHostedService<WorkerService>();

var host = builder.Build();
host.Run();
```

Worker services use `IServiceScopeFactory` for scoped dependencies:

```csharp
internal sealed class WorkerService(
    IServiceScopeFactory scopeFactory,
    IConnection rabbitConnection,
    ILogger<WorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        // ... use scoped services
    }
}
```

## Phase 5: Aspire Wiring

In `AppHost/Program.cs`, add the new context's resources:

```csharp
var {context}Db = builder.AddPostgres("{context}-db").AddDatabase<{context}DbContext>("{context}db");
var {context}Migration = builder.AddProject<Projects.SchoolCollab_{Context}_MigrationService>("{context}-migration")
    .WithReference({context}Db);
var {context}Api = builder.AddProject<Projects.SchoolCollab_{Context}_Api>("{context}-api")
    .WithReference({context}Db).WithReference(rabbit).WithReference(redis)
    .WaitFor({context}Migration);
var {context}Worker = builder.AddProject<Projects.SchoolCollab_{Context}_Worker>("{context}-worker")
    .WithReference({context}Db).WithReference(rabbit).WithReference(redis)
    .WaitFor({context}Migration);
var admin = builder.AddProject<Projects.SchoolCollab_Admin>("admin")
    .WithReference({context}Api)
    .WaitFor({context}Api);
```

## Phase 6: Testing

Unit tests go in `tests/SchoolCollab.{Context}.Tests.Unit/`. Use MSTest
(`[TestClass]`/`[TestMethod]`), Moq for mocking, and FluentAssertions.

- One test class per handler: `CreateStudentHandlerTests.cs`
- Domain entity tests are pure unit tests (no mocking)
- `<InternalsVisibleTo Include="SchoolCollab.{Context}.Tests.Unit" />` in `.csproj`
- Test happy path, edge cases, and error paths

## Conventions Summary

| Convention | Rule |
|---|---|
| Target framework | `net10.0` |
| CPM | All versions in `Directory.Packages.props`; no `Version` on `PackageReference` |
| Logging | Serilog → OTLP → Aspire; no `Console.WriteLine`; no `AddConsole()` |
| Render mode | Set once in `App.razor`; no per-page `@rendermode` |
| Loading state | Items-null pattern, not `_loading` bool |
| Optimistic UI | Mutate in-memory, call API, rollback on failure |
| IDisposable | Every page with `OnInitializedAsync` owns a `CancellationTokenSource` |
| `_disposed` guard | Check after every `await` to prevent renderer race |
| FluentUI | No Bootstrap elements |
| `@key` | On every `@foreach` rendering components |
| EF Migrations | One per PR; review for unintended drops; always implement `Down()` |
| Repository pattern | Public interface, `internal sealed` implementation |
| Integration events | Domain events implement `IDomainEvent`; integration events are plain records in Contracts |

## Reference Files

| File | Contents |
|---|---|
| [references/architecture.md](references/architecture.md) | Detailed project structure, naming, and dependency rules |
| [references/blazor-page-template.md](references/blazor-page-template.md) | Full page template with all required patterns |
| [references/api-endpoint-template.md](references/api-endpoint-template.md) | Full minimal API endpoint patterns |