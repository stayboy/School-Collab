# Copilot Instructions — SchoolCollab

These instructions apply to every file in this repository.

---

## Skill discovery (read first)

When you need a skill — for code review, PR description, testing, deployment,
documentation, design review, etc. — **always start at one of these two
canonical catalogs** before any other source:

1. **[https://awesome-copilot.github.com/skills/](https://awesome-copilot.github.com/skills/)**
   — community-curated Copilot skills (Skill `name`/description metadata, with a
   machine-readable `llms.txt` at
   [https://awesome-copilot.github.com/llms.txt](https://awesome-copilot.github.com/llms.txt)).
   Skills live at
   `https://raw.githubusercontent.com/github/awesome-copilot/main/skills/<skill-name>/SKILL.md`.
2. **[https://github.com/microsoft/skills](https://github.com/microsoft/skills)**
   — Microsoft-authored skills, MCP servers, and tools. Use this catalog when
   looking for first-party Microsoft patterns (Aspire, Azure SDKs, .NET,
   TypeScript/JS, etc.) or for the official Microsoft MCP / tool
   implementations that ship alongside a service.

Workflow:

- Pick the catalog that best matches the source: **awesome-copilot** for
  community/third-party patterns, **microsoft/skills** for first-party
  Microsoft/Microsoft-owned tooling.
- Search the chosen catalog (use `llms.txt` for awesome-copilot when doing bulk
  discovery). For microsoft/skills, browse the repo's `skills/`, `mcp/`, and
  `tools/` directories.
- If a suitable skill exists, use it (or install it via the documented install
  command) before falling back to ad-hoc authoring.
- If the catalog has nothing relevant, say so explicitly, then propose a
  custom approach. Do not silently swap in a different source (e.g.
  `awesome-skills`, `kevintsengtw/*`, etc.) without an explicit user
  request.

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

The render mode is set **once** for the entire app on `<Routes>` and `<HeadOutlet>` in
`Components/App.razor`. Pages **must not** declare `@rendermode` themselves — they
inherit the global default.

```razor
@* ✅ correct — declared once in App.razor, inherited by every page *@
<HeadOutlet @rendermode="new InteractiveServerRenderMode(prerender: false)" />
<Routes @rendermode="new InteractiveServerRenderMode(prerender: false)" />

@* ❌ wrong — per-page rendermode drifts and re-enables prerendering *@
@rendermode InteractiveServer
```

The default for this project is **Interactive Server with pre-rendering disabled**:

- **Pre-rendering is off at the app level** so `OnInitializedAsync` runs exactly once
  (no duplicate API calls, no duplicate component-tree construction). In .NET 10,
  `InteractiveServer` *with* prerendering would run `OnInitializedAsync` twice and
  double every API call.
- The list-bearing landing page therefore relies on `OnInitializedAsync` to populate
  the first render directly, with `<FluentProgressRing />` as the loading state.

If a single page genuinely needs a different mode (e.g. static SSR, or a child
component that must opt out), declare `@rendermode` on **that page or instance
only** and add a comment explaining why. The pre-render flag of a child is
ignored when a parent already specifies a render mode, so app-wide changes
must be made in `App.razor`.

For the rationale (and the renderer race that was the original motivation), see
the rule below and the "Landing-page performance pattern" block further down.

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

1. **Interactive Server with pre-render disabled — the app's default.** The list page
   has event handlers (`OnClick` for navigation, `OnToggleAsync` for Enable/Disable)
   that require an interactive circuit. It inherits the global render mode from
   `App.razor` and does **not** declare its own `@rendermode`. Do not add
   `[StreamRendering(true)]` or a per-page `@rendermode` to list pages — see the
   "Render mode and pre-rendering" rule above.

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
       private volatile bool _disposed;

       protected override async Task OnInitializedAsync()
       {
           _loadCts = new CancellationTokenSource();
           try
           {
               _items = await Api.GetRootValuesAsync(_loadCts.Token);
               if (_disposed) return;   // <-- ALWAYS guard post-await state writes
               Logger.LogInformation("Loaded {Count}", _items?.Length ?? 0);
           }
           catch (OperationCanceledException) { /* user navigated away */ }
           catch (Exception ex)
           {
               if (_disposed) return;   // <-- and again on the error path
               _error = ex.Message;
           }
       }

       public void Dispose()
       {
           _disposed = true;            // <-- set this FIRST so the continuation
                                        //     sees it even if Cancel() races
           _loadCts?.Cancel();
           _loadCts?.Dispose();
           _loadCts = null;
       }
   }
   ```
   **Why the `_disposed` guard matters:** even with the CTS, the awaited continuation
   can still run after the renderer is torn down (e.g. the HTTP response has flushed
   its placeholder and the streaming renderer was discarded). Setting `_disposed = true`
   inside `Dispose()` and checking it *after every `await`* prevents mutating state on
   a detached component, which would otherwise throw
   `ArgumentException: "The renderer does not have a component with ID {N}"` from
   `Renderer.GetRequiredComponentState`. The `_disposed` flag must be checked after
   the `await` in `OnInitializedAsync` *and* before every `StateHasChanged()` in
   event handlers that may still be in-flight (e.g. optimistic-toggle rollback).
   See `CodedValuesRendererRaceTests` for a Playwright test that reliably reproduces
   the race by slowing the API with `page.route()` and triggering a second
   navigation.

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

## CSS and styling

### Rule: Prefer Blazor CSS isolation over global CSS

Every Razor component (pages, layouts, dialogs) **must** have a companion `.razor.css`
file for component-scoped styles. Never add page-specific styles to `wwwroot/app.css`.

```
Components/Pages/CodedValues/Index.razor      ← markup
Components/Pages/CodedValues/Index.razor.css  ← scoped styles
```

**Why?** Blazor CSS isolation generates unique scope identifiers (e.g. `b-xyz`) so
styles in `Index.razor.css` only apply to `Index.razor` — no class-name collisions,
no specificity wars, and no dead CSS when a page is removed.

### Rule: Prefer CSS classes over inline `style`

Never use `style="…"` on HTML elements in `.razor` files. Extract the styles into
a named class in the component's `.razor.css` file:

```razor
@* ❌ wrong *@
<div style="flex:1; min-height:0; overflow:auto;">

@* ✅ correct *@
<div class="grid-container">
```

```css
/* In Component.razor.css */
.grid-container {
    flex: 1;
    min-height: 0;
    overflow: auto;
}
```

The only exceptions are truly one-off dynamic values computed in C# (e.g. a
`width` or `color` derived from data) — even then, prefer a CSS custom property
set via `style="--my-value: @value"` and reference it in the isolated CSS.

### Rule: `::deep` for child-component and web-component selectors

Blazor CSS isolation scopes selectors to the *component's own* elements. To target
elements rendered by child components (including FluentUI web components like
`<fluent-data-grid>`), use `::deep`:

```css
/* In Index.razor.css — targets cells inside FluentDataGrid */
::deep .grid-sticky-cols td[col-index="1"] {
    position: sticky;
    left: 0;
}
```

Without `::deep`, the scope attribute would be placed on the `td` itself, which
doesn't exist in the component's direct markup.

### Rule: Global styles in `wwwroot/app.css`

Only add styles to `wwwroot/app.css` when they are truly application-wide:

| Keep in `app.css` (global) | Move to `.razor.css` (isolated) |
|---|---|
| `html, body` resets | Page layout (flex containers, grid wrappers) |
| Theme / colour variables | Toolbar actions, button bars |
| `fluent-data-grid` global parts | Spinner / loading containers |
| `.grid-sticky-cols` (shared grid pattern) | Form field layouts |
| `.link-action`, `.brand*` | Dialog / chat bubble styles |
| Responsive breakpoints for `.brand*` | Any style used by exactly one component |

If a style is used by two or more components, consider whether it should be a
shared CSS class in `app.css` (e.g. utility classes) or duplicated in each
isolated file (preferred if the styles may diverge).

### Rule: FluentUI components — use component parameters, not CSS

Prefer FluentUI's built-in parameters over custom CSS for spacing, alignment, and
layout on `<Fluent*>` components:

| Instead of | Use |
|---|---|
| `style="width:100%"` on `<FluentTextField>` | `Style="width:100%"` parameter (still inline) or a class in isolated CSS |
| `style="margin:0"` on `<h1>` | A class like `.page-title { margin: 0; }` in isolated CSS |

When FluentUI provides a parameter (e.g. `Appearance`, `Gap`, `Orientation`), use
it instead of replicating the same effect in CSS.

---

## Bug-fix regression tests

Every bug fix must include a regression test that proves the reported bug is fixed.
Do not treat a bug fix as complete when it only changes production code.

### Rules

1. **Write the regression test first when practical.** The test should fail against the
   buggy code and pass after the fix. If reproducing the exact failure is too expensive,
   add the smallest test that covers the fixed behaviour and explain the trade-off in the
   PR description.

2. **Run the relevant test project after the fix.** At minimum, run the test project that
   owns the changed production code before committing. If the fix crosses projects, run
   all affected test projects.

3. **Backend and domain bugs.** Add or update unit/integration tests using the existing
   MSTest/Moq/FluentAssertions patterns. API/client bug fixes should include HTTP status,
   payload, and error-path coverage where applicable.

4. **UI and Blazor component bugs.** Use **bUnit** tests for Razor/Blazor component
   regressions. Test the rendered component tree and user-facing behaviour, not only
   private methods or view models.

   - Add `bunit` packages to the test project that owns the component if they are not
     already present.
   - Register required services (`NavigationManager`, dialog/toast providers, HTTP
     clients, etc.) in the bUnit `TestContext`.
   - Assert the bug-specific UI outcome, such as route discovery, expected headings,
     buttons, empty states, error boundaries, or disabled actions.

5. **No untested bug fixes.** If a bug cannot be tested directly, document why in the PR
   and add the closest available coverage, such as routing, service, or component
   integration coverage.

---

## Unit tests for feature additions

Every new feature, service, or behavioural class **must** include unit tests in
`tests/SchoolCollab.CodedValues.Tests.Unit/`. Tests go in a file named after the class
under test (e.g. `ChatClientFactoryTests.cs` for `ChatClientFactory.cs`).

### Rules

1. **Add tests alongside new code.** A PR that adds a new class with behavioural logic
   (routing, validation, text cleaning, mapping, etc.) must also add a corresponding test
   file or extend an existing one. Pure data-transfer objects (DTOs, records) and trivial
   wrappers (delegates, thin extension methods) are exempt.

2. **Test file naming.** `<ClassName>Tests.cs` — one test class per production class.
   Keep tests in the root `SchoolCollab.CodedValues.Tests.Unit` namespace unless a
   `Domain/` subfolder matches the production namespace.

3. **Framework.** Use MSTest (`[TestClass]`/`[TestMethod]`), Moq for mocking, and
   FluentAssertions for assertions. These packages are already referenced.

4. **Coverage targets.** At minimum, test:
   - **Happy path** — the primary use case works correctly.
   - **Edge cases** — null/empty inputs, boundary values, case sensitivity.
   - **Error/fallback paths** — what happens when a dependency is missing or returns
     an unexpected result.
   - **Routing/branching logic** — every `if`/`switch` branch must have at least one
     test that exercises it (e.g. local vs cloud routing in `ChatClientFactory`).

5. **Run tests before committing.** `dotnet test` must pass with 0 failures before a PR
   is submitted. If existing tests break, fix them in the same commit.

6. **Reference the production project.** The unit test project already references
   `SchoolCollab.AI` and `SchoolCollab.CodedValues.Core` via
   `<ProjectReference>`. Add a new reference only if the new production code lives in a
   different project.

7. **`InternalsVisibleTo`.** If the class under test is `internal`, ensure the production
   project has `<InternalsVisibleTo Include="SchoolCollab.CodedValues.Tests.Unit" />` in
   its `.csproj`.

8. **HTTP 404 handling pattern.** When an API client method calls an endpoint that may
   return 404 (e.g. "get by code" or "get by id"), the method must check
   `response.StatusCode == HttpStatusCode.NotFound` and return `null` instead of
   throwing `HttpRequestException`. Never use `GetFromJsonAsync<T>()` for endpoints
   that can return 404 — it throws on non-success status codes. Use `GetAsync()` +
   status check + `ReadFromJsonAsync<T>()` instead.
