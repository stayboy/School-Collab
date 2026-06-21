# Logging and Aspire Observability

All logging in this project flows through **Serilog** wired to **Aspire's OTLP
pipeline**. Every log from every service — backend API *and* Blazor frontend — must be
visible in the Aspire dashboard structured log viewer.

## Rules (apply to all services)

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

## Backend API (`SchoolCollab.CodedValues.Api`)

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

## Blazor Frontend (`SchoolCollab.CodedValues.Admin`)

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

## Domain / Core (`SchoolCollab.CodedValues.Core`)

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

## Aspire Dashboard visibility

The pipeline is: **Serilog → OTLP gRPC → Aspire dashboard**.

- Works automatically when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (Aspire injects this).
- In local development outside Aspire, logs fall back to the console sink only.
- **Do not add a separate `appsettings.json` Serilog section** unless overriding minimum
  levels per environment — the code configuration in `Extensions.cs` is the source of
  truth.
