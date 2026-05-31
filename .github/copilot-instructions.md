# Copilot Instructions — SchoolCollab

These instructions apply to every file in this repository.

---

## Logging

All logging in this project flows through **Serilog** wired to **Aspire's OTLP pipeline**.
Every log from every service — backend API *and* Blazor frontend — must be visible in the
Aspire dashboard structured log viewer.

### Rules (apply to all services)

1. **Never use `Console.WriteLine`, `Debug.WriteLine`, or `Trace.Write`** — always use
   `ILogger<T>` injected via dependency injection.

2. **Never add `builder.Logging.AddConsole()` or `builder.Logging.AddOpenTelemetry()`
   directly** — logging is centralised in `SchoolCollab.ServiceDefaults/Extensions.cs`
   via `ConfigureSerilog()`. Adding extra providers causes duplicate log entries.

3. **`builder.AddServiceDefaults()` must be the first call** in every `Program.cs`. This
   ensures Serilog is initialised before any other middleware.

4. **Use structured logging with named properties**, not string interpolation:
   ```csharp
   // ✅ correct
   _logger.LogInformation("Coded value {CodedValueId} created by {UserId}", id, userId);

   // ❌ wrong
   _logger.LogInformation($"Coded value {id} created by {userId}");
   ```

5. **Log levels**:
   | Level | When to use |
   |---|---|
   | `LogTrace` | Fine-grained internal steps (dev only) |
   | `LogDebug` | Inputs/outputs of queries and commands |
   | `LogInformation` | Significant business events (value created, disabled, etc.) |
   | `LogWarning` | Recoverable issues, validation failures, retries |
   | `LogError` | Unhandled exceptions, infrastructure failures |

6. **Always pass the exception object** as the first argument to `LogError`/`LogWarning`:
   ```csharp
   // ✅ correct — Serilog serialises the full exception
   _logger.LogError(ex, "Failed to create coded value {Code}", command.Code);
   ```

### Backend API (`SchoolCollab.CodedValues.Api`)

- `app.UseSerilogRequestLogging()` **must remain** between `app.MapDefaultEndpoints()`
  and the first `app.MapGet/Post/…` call. It emits one structured log per HTTP request
  including method, path, status code, and elapsed time.

- Minimal-API endpoint handlers should **not** log individual request fields — the
  middleware already captures them. Log only domain outcomes:
  ```csharp
  app.MapPost("/coded-values", async (CreateCodedValueCommand cmd, ...) =>
  {
      var id = await handler.HandleAsync(cmd, ct);
      logger.LogInformation("CodedValue created {Id}", id);
      return Results.Created($"/coded-values/{id}", id);
  });
  ```

### Blazor Frontend (`SchoolCollab.CodedValues.Admin`)

- Inject `ILogger<T>` in every page component and service class.

- Log page lifecycle events that are meaningful for debugging:
  ```csharp
  @inject ILogger<Index> Logger

  protected override async Task OnInitializedAsync()
  {
      Logger.LogInformation("Loading coded values list");
      // ...
      Logger.LogInformation("Loaded {Count} coded values", items.Length);
  }
  ```

- Log user actions that trigger backend calls:
  ```csharp
  Logger.LogInformation("User initiated create for code {Code}", Model.Code);
  ```

- Log errors from API calls with full exception:
  ```csharp
  catch (Exception ex)
  {
      Logger.LogError(ex, "Failed to save coded value {Code}", Model.Code);
  }
  ```

### Domain / Core (`SchoolCollab.CodedValues.Core`)

- Command handlers must log command receipt and outcome:
  ```csharp
  public async Task<Guid> HandleAsync(CreateCodedValueCommand cmd, CancellationToken ct)
  {
      _logger.LogDebug("Handling CreateCodedValue {Code}", cmd.Code);
      // ...
      _logger.LogInformation("CodedValue {Id} persisted with code {Code}", entity.Id, cmd.Code);
      return entity.Id;
  }
  ```

- Query handlers log at `Debug` level only (high-frequency, low noise).

### Aspire Dashboard visibility

The pipeline is: **Serilog → OTLP gRPC → Aspire dashboard**.

- Works automatically when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (Aspire injects this).
- In local development outside Aspire, logs fall back to the console sink only.
- **Do not add a separate `appsettings.json` Serilog section** unless overriding minimum
  levels per environment — the code configuration in `Extensions.cs` is the source of truth.

---

## Blazor component best practices

### Render mode and pre-rendering

All interactive pages use `@rendermode InteractiveServer`. In .NET 10, this pre-renders
via SSR *then* re-renders once the SignalR circuit connects — meaning `OnInitializedAsync`
runs **twice** and all API calls are made twice.

**Always disable pre-rendering** on interactive pages to prevent duplicate API calls:

```razor
@* ✅ correct — no double execution *@
@rendermode @(new InteractiveServerRenderMode(prerender: false))

@* ❌ wrong — causes OnInitializedAsync to run twice *@
@rendermode InteractiveServer
```

For static display-only pages, use `@attribute [StreamRendering(true)]` instead — this
sends the loading placeholder immediately over HTTP and streams real content when the data
returns, giving the fastest perceived load without a SignalR circuit.

### Parallel data loading

Never `await` independent API calls sequentially. Use `Task.WhenAll` to fire them in
parallel:

```csharp
// ❌ wrong — serial, pays full latency twice
_parent   = await Api.GetByIdAsync(ParentId);
_children = await Api.GetChildrenAsync(ParentId);

// ✅ correct — parallel, pays latency once
var parentTask   = Api.GetByIdAsync(ParentId);
var childrenTask = Api.GetChildrenAsync(ParentId);
await Task.WhenAll(parentTask, childrenTask);
_parent   = parentTask.Result;
_children = childrenTask.Result;
```

### Loading states

Use `<FluentProgressRing />` consistently across **all** pages — never use bare
`<p><em>Loading…</em></p>`. Every page must show a spinner while `OnInitializedAsync`
is pending:

```razor
@if (_loading)
{
    <FluentProgressRing />
}
else if (_items is { Length: 0 })
{
    <FluentMessageBar Intent="MessageIntent.Info">No items yet.</FluentMessageBar>
}
```

### Error boundaries

Wrap the primary content of every page in `<ErrorBoundary>` so unhandled exceptions
show a recovery UI instead of crashing the whole layout:

```razor
<ErrorBoundary>
    <ChildContent>
        @* main page content *@
    </ChildContent>
    <ErrorContent Context="ex">
        <FluentMessageBar Intent="MessageIntent.Error">
            Something went wrong: @ex.Message
        </FluentMessageBar>
    </ErrorContent>
</ErrorBoundary>
```

### `@key` on dynamic lists

Any `@foreach` that renders child components or elements **must** include `@key` so
Blazor's diffing algorithm can reuse existing DOM nodes instead of re-creating them:

```razor
@* ✅ correct *@
@foreach (var attr in context.Attributes)
{
    <FluentBadge @key="attr.Key">@attr.Key=@attr.Value</FluentBadge>
}

@* ❌ wrong — no @key, causes unnecessary DOM teardown *@
@foreach (var attr in context.Attributes)
{
    <FluentBadge>@attr.Key=@attr.Value</FluentBadge>
}
```

### Component parameters

- Use `[Parameter, EditorRequired]` for required parameters — IDE warns if omitted.
- Use `EventCallback<T>` (not `Action<T>`) for child-to-parent events — async-safe and
  automatically calls `StateHasChanged`.
- Implement `IAsyncDisposable` to clean up timers, subscriptions, and JS module references.

### Consistent UI — FluentUI only

Do **not** mix Bootstrap HTML elements with FluentUI. Replace every Bootstrap element:

| Bootstrap | FluentUI replacement |
|---|---|
| `<input class="form-control">` | `<FluentTextField>` |
| `<textarea class="form-control">` | `<FluentTextArea>` |
| `<input type="number">` | `<FluentNumberField>` |
| `<button class="btn btn-primary">` | `<FluentButton Appearance="Appearance.Accent">` |
| `<button class="btn btn-secondary">` | `<FluentButton Appearance="Appearance.Outline">` |
| `<div class="alert alert-danger">` | `<FluentMessageBar Intent="MessageIntent.Error">` |

### ShouldRender optimisation

Override `ShouldRender()` on components that receive frequent external events but only
need to update their DOM when their own data changes:

```csharp
private bool _dataChanged;

protected override bool ShouldRender()
{
    if (!_dataChanged) return false;
    _dataChanged = false;
    return true;
}
```

---

## Entity Framework Core migrations

**Any change to a domain entity, owned type, value object, or `IEntityTypeConfiguration` that affects the database schema must be accompanied by a new EF migration before the PR is merged.**

### DbContexts in this repository

| DbContext | Project | Migrations folder |
|---|---|---|
| `CodedValuesDbContext` | `src/CodedValues/SchoolCollab.CodedValues.Core` | `Data/Migrations/` |

Each bounded context that owns a `DbContext` follows the same pattern.

### Adding a migration

Run from the **repository root**:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/CodedValues/SchoolCollab.CodedValues.Core \
  --context CodedValuesDbContext
```

Use a descriptive PascalCase name that summarises the schema change, e.g.
`AddAttributeDefinitionAllowMultiple`, `AddValidationFieldsToCodedValue`.

`IDesignTimeDbContextFactory<T>` is already implemented in each Core project — no
startup project or connection string flag is needed.

### Removing the last migration (if not yet applied)

```bash
dotnet ef migrations remove \
  --project src/CodedValues/SchoolCollab.CodedValues.Core \
  --context CodedValuesDbContext
```

### Rules

1. **Never modify an existing migration file** — always add a new one. Editing applied
   migrations corrupts the migration history.

2. **Always review the generated migration** before committing. EF sometimes drops and
   recreates columns instead of altering them; rename operations require manual
   `migrationBuilder.RenameColumn` / `RenameTable` calls.

3. **Migrations live in the Core project**, not the API or MigrationService projects.
   The `MigrationService` applies them at startup — it does not own them.

4. **One migration per logical change** — do not batch unrelated model changes into a
   single migration.

5. **Verify the snapshot is updated** — `dotnet ef migrations add` regenerates
   `<ContextName>ModelSnapshot.cs` automatically; commit it alongside the migration.

---

## Target framework

All projects target **net10.0**. Do not downgrade to net9.0 or earlier.

## Architecture reminders

- No direct project references between bounded contexts — use MassTransit contracts.
- No MediatR — CQRS is implemented via `ICommandHandler<T>` / `IQueryHandler<T,R>` with
  Scrutor assembly scanning.
- Domain entities use PostgreSQL `xmin` (row version) for optimistic concurrency.
