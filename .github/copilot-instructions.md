# Copilot Instructions — SchoolCollab

These instructions apply to every file in this repository.

---

## Skill discovery (read first)

When you need a skill — for code review, PR description, testing, deployment,
documentation, design review, etc. — **always start at
[https://awesome-copilot.github.com/skills/](https://awesome-copilot.github.com/skills/)**.

- The catalog is searchable; a machine-readable `llms.txt` is available at
  [https://awesome-copilot.github.com/llms.txt](https://awesome-copilot.github.com/llms.txt)
  for bulk skill discovery.
- Skills live at `https://raw.githubusercontent.com/github/awesome-copilot/main/skills/<skill-name>/SKILL.md`
  — fetch the raw `SKILL.md` to read or quote one.
- If a suitable skill exists in the catalog, use it (or install it via
  `copilot plugin install <skill>@awesome-copilot`) before falling back to ad-hoc
  authoring or other registries.
- Do **not** invent skills from third-party registries (e.g. `awesome-skills`,
  `kevintsengtw/*`, etc.) without an explicit user request — the awesome-copilot
  catalog is the single source of truth.
- If the catalog has nothing relevant, say so explicitly, then propose a custom
  approach. Do not silently swap in a different source.

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

### Landing-page performance pattern

The coded-values landing page (`Components/Pages/CodedValues/Index.razor`) is the
**canonical example** for read-only public pages. Every new read-only list/detail page
**must** follow the same pattern:

1. **Stream-render, not InteractiveServer.** The list is the same for every visitor —
   there is no per-user state, no form bindings, no event handlers that need a
   SignalR circuit. Use:
   ```razor
   @attribute [StreamRendering(true)]
   ```
   First paint arrives in one network round-trip (HTML + data over a single response).
   Reserve `@rendermode @(new InteractiveServerRenderMode(prerender: false))` for
   pages that have form state (`Create.razor`, `Edit.razor`).

2. **Optimistic UI mutations, not full re-fetch.** When a row action changes a single
   field (Enable/Disable, Toggle, Increment), mutate the in-memory DTO with `with`,
   call `StateHasChanged()`, dispatch the API call in the background, and roll back
   on failure. **Never call `LoadAsync()` after a single-row mutation** — that
   re-serialises the entire list and resets the user's sort/scroll state.
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
           Logger.LogError(ex, "Failed to toggle coded value {Id}", id);
           var i = Array.FindIndex(_items, x => x.Id == id);
           if (i >= 0) _items[i] = previous;   // rollback
           _error = ex.Message;
           StateHasChanged();
       }
   }
   ```

3. **Always implement `IDisposable` and pass a `CancellationToken`.** Every page that
   loads data in `OnInitializedAsync` must own a `CancellationTokenSource`, pass its
   token into every `Api.*Async(ct)` call, and dispose the CTS in `Dispose()`. This
   aborts the in-flight HTTP request when the user navigates away and prevents
   setting state on an unmounted component.
   ```csharp
   @implements IDisposable

   @code {
       private CancellationTokenSource? _loadCts;

       protected override async Task OnInitializedAsync()
       {
           _loadCts = new CancellationTokenSource();
           try { _items = await Api.GetRootValuesAsync(_loadCts.Token); }
           catch (OperationCanceledException) { /* user navigated away */ }
           catch (Exception ex) { _error = ex.Message; }
       }

       public void Dispose() { _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = null; }
   }
   ```

4. **Use the "items null" pattern for loading state**, not a `_loading` bool. The
   streaming-rendering placeholder is shown automatically while `_items is null`.
   The `else if (_items.Length == 0)` branch handles the empty case.
   ```razor
   @if (_items is null)         { <FluentProgressRing /> }
   else if (_items.Length == 0) { <FluentMessageBar>No items yet.</FluentMessageBar> }
   else                         { <FluentDataGrid ... /> }
   ```

5. **Keep the slim payload on the landing endpoint.** The landing page never renders
   `Attributes` — don't transfer them. If the current DTO includes
   `IReadOnlyCollection<CodedValueAttributeDto>`, add a `CodedValueSummaryDto`
   projection to the API and a matching `GetRootSummariesAsync()` on the client.
   (Pending implementation — tracked in `lp-slim-dto` todo.)

6. **Reference implementation:** `src/CodedValues/SchoolCollab.CodedValues.Admin/Components/Pages/CodedValues/Index.razor`.
   When you create a new read-only list page, copy that file and change only the
   route, the title, the API call, and the columns.

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

`IDesignTimeDbContextFactory<T>` is already implemented in each Core project — no
startup project or connection string flag is needed.

### Removing the last migration (if not yet applied)

```bash
dotnet ef migrations remove \
  --project src/CodedValues/SchoolCollab.CodedValues.Core \
  --context CodedValuesDbContext
```

### Naming conventions

Use **PascalCase** names following the `{Verb}{Entity}{Property}` pattern — treat the
migration name like a commit message that describes the schema change precisely:

| ✅ Good | ❌ Bad |
|---|---|
| `AddCodedValueParentId` | `Update1` |
| `RemoveObsoleteAuditColumns` | `Fixes` |
| `AddAttributeDefinitionAllowMultiple` | `UpdateCodedValue` |
| `CreateCodedValueAttributeDefinitionTable` | `Migration20260531` |

Use the `Seed` prefix for migrations that insert reference/lookup data:
`SeedHospitalTypeCodes`.

### Rules

1. **Never modify an existing migration file** — always add a new one. Editing applied
   migrations corrupts the migration history.

2. **Never edit the `.Designer.cs` file** — it is auto-generated by EF Core. If it is
   wrong, remove the migration and re-add it.

3. **Always review the generated migration** before committing. EF sometimes drops and
   recreates columns instead of altering them; rename operations require manual
   `migrationBuilder.RenameColumn` / `RenameTable` calls.

4. **Always implement `Down()`** — even if it is a no-op comment, never leave `Down()`
   empty or broken. Reversal order must be the inverse of `Up()`: if `Up()` runs
   A → B → C then `Down()` must run C → B → A.

5. **Migrations live in the Core project**, not the API or MigrationService projects.
   The `MigrationService` applies them at startup — it does not own them.

6. **One migration per PR** — do not batch unrelated model changes into a single
   migration, and do not submit multiple migrations in one PR unless they form a
   single indivisible feature change.

7. **Verify the snapshot is updated** — `dotnet ef migrations add` regenerates
   `<ContextName>ModelSnapshot.cs` automatically; commit it alongside the migration.
   A merge conflict in `*ModelSnapshot.cs` means two branches added migrations
   concurrently — resolve by running `dotnet ef migrations remove` on your branch,
   merging the other branch first, then re-adding your migration.

8. **New schema must be backward-compatible** with the previous app version — deploying
   a migration must never break the currently-running app. Additive changes (new
   nullable columns, new tables) are safe. Removing or renaming columns requires a
   multi-step release.

### Pending model-changes guard (EF Core 8+)

Add this unit test to every project that owns a `DbContext`. It catches commits where
a model change was made but the migration was forgotten:

```csharp
[Fact]
public void NoUncommittedModelChanges()
{
    using var context = new CodedValuesDbContext(
        new DbContextOptionsBuilder<CodedValuesDbContext>()
            .UseNpgsql("Host=localhost;Database=guard") // DSN irrelevant — snapshot-only check
            .Options);

    Assert.False(
        context.Database.HasPendingModelChanges(),
        "Model has changes not reflected in a migration. " +
        "Run 'dotnet ef migrations add <Name> --project src/CodedValues/SchoolCollab.CodedValues.Core'");
}
```

This check compares the compiled model against `*ModelSnapshot.cs` — no live database
connection is required.

### Data seeding vs schema migrations

Keep seeding and schema changes separate:

| Pattern | Use for |
|---|---|
| `HasData(...)` in `IEntityTypeConfiguration` | Truly static lookup/reference data (enum tables, country codes). Primary keys must be hardcoded. |
| `UseAsyncSeeding` on `DbContextOptionsBuilder` | Dev/staging seed data, identity setup, data requiring runtime state or generated keys. |
| `migrationBuilder.Sql(...)` inside a migration | Data transforms that must run atomically with a schema change (backfill before adding NOT NULL, column splits). Always provide the reverse in `Down()`. |

Never insert application seed data (users, test records) inside a schema migration file.

---

## Central Package Management (CPM)

All NuGet package versions are managed centrally in **`Directory.Packages.props`** at the
repository root. This prevents version drift across the 10-project solution.

### How it works

`Directory.Build.props` sets `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`.
All `<PackageReference>` elements in `.csproj` files **must not** include a `Version`
attribute — the version is resolved from `Directory.Packages.props`.

### Adding a new package

1. Add a `<PackageVersion>` entry in `Directory.Packages.props` under the appropriate
   label group:
   ```xml
   <PackageVersion Include="My.New.Package" Version="1.2.3" />
   ```

2. Add `<PackageReference Include="My.New.Package" />` (no `Version`) in the target
   `.csproj`.

3. **Never** add `Version="..."` directly to a `<PackageReference>` — CPM will raise
   NU1008 / NU1009 errors at build time if you do.

### Updating a package version

Change the version only in `Directory.Packages.props`. The update applies to every
project that references it automatically.

### Exceptions

- `PrivateAssets="all"` (and other metadata attributes like `IncludeAssets`, `ExcludeAssets`)
  **stay** in the `<PackageReference>` element inside the `.csproj` — they are not version
  metadata and are not moved to `Directory.Packages.props`.
  ```xml
  <!-- csproj -->
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
  ```

- `<Sdk Name="..." Version="..." />` at the top of a `.csproj` (e.g. `Aspire.AppHost.Sdk`)
  is an **MSBuild SDK reference**, not a NuGet package — CPM does not manage it, leave the
  `Version` attribute in place.

---

## Target framework

All projects target **net10.0**. Do not downgrade to net9.0 or earlier.

## Architecture reminders

- No direct project references between bounded contexts — use MassTransit contracts.
- No MediatR — CQRS is implemented via `ICommandHandler<T>` / `IQueryHandler<T,R>` with
  Scrutor assembly scanning.
- Domain entities use PostgreSQL `xmin` (row version) for optimistic concurrency.
