# AI Services

Rules for the `SchoolCollab.AI` project (`src/SchoolCollab.AI`) and any code that
calls into it (Blazor admin pages, the `/api/ai/chat` HTTP endpoint, tests).

## Architecture

The AI layer is **transport-agnostic, provider-pluggable, and encapsulated**.
New code MUST route through these seams instead of constructing clients
directly:

| Concern | Seam | Notes |
|---|---|---|
| Pick the active provider (Ollama / OpenRouter) | `IChatClientFactory.GetClient()` | Never `new OpenAIClient(...)` or `new OllamaApiClient(...)` outside `ChatClientFactory`. |
| Resolve `(provider, model)` from three config values | `ChatModelResolver.Resolve(provider, ollamaModel, openRouterModel)` | Returns `(string Provider, string Model)`. Pure function. |
| Stream updates from the model | `CodedValueAIService.ChatAsync(history, ct)` | Yields `ChatUpdate` records. The service resolves the model itself; callers MUST NOT pass a model string. |
| Talk to the API from a Blazor page | `AiChatClient` (in `SchoolCollab.Admin.Shared`) | Thin HTTP wrapper around `/api/ai/chat` and `/api/ai/config`. |
| Display the active provider/model in a Blazor page | `AiChatClient.GetConfigurationAsync()` | Returns `(DefaultProvider, DefaultModel)`. |

### The AI service is the single source of truth for its configuration

- The AI service is hosted in its own process (`SchoolCollab.AI/Program.cs`).
  All AI configuration (`codedvalue-ai-provider`, `Ollama:*`, `OpenRouter:*`)
  is read from THAT host's configuration — not from any admin host.
- Admin hosts (`SchoolCollab.Admin`, `SchoolCollab.CodedValues.Admin`,
  etc.) **MUST NOT register `CodedValueAIService`, `IChatClientFactory`,
  `ChatModelResolver`, or `IConfiguration`-based AI resolvers in their DI
  container.** Those types live behind the AI host's HTTP boundary.
- Admin pages that want to display the active provider/model MUST call
  `AiChatClient.GetConfigurationAsync()` (HTTP to `/api/ai/config`), not
  inject the AI service directly. This keeps the modular boundary intact and
  means admin pages work even if the AI host is unreachable (the existing
  `ErrorBoundary` wraps the chat UI).
- `Program.cs` MUST NOT hardcode provider or model strings — they flow from
  `appsettings.json` via `ChatModelResolver`.
- HTTP endpoints inside the AI host MUST NOT accept a model from the client.
  The server resolves the model from configuration inside `ChatAsync`.

## Coding Standards

- **Strong typing for AI tool definitions.** Tool metadata (`name`,
  `description`, parameter shapes) lives in `_tools` inside
  `CodedValueAIService` and is wired up via `AIFunctionFactory.Create(...)`.
  Don't construct `AITool` instances by hand unless the call site genuinely
  needs a custom `AIFunction` subclass.
- **Use `Microsoft.Extensions.AI` abstractions** (`IChatClient`, `ChatOptions`,
  `ChatMessage`, `ChatRole`, `TextContent`, `FunctionCallContent`,
  `FunctionResultContent`). Do not pull provider-specific types
  (`OpenAI.Chat.ChatCompletion`, etc.) into `SchoolCollab.AI` business logic.
- **Provider-specific SDKs are confined to `ChatClientFactory`.** That is the
  only place `OpenAIClient` / Ollama clients are constructed.
- **Configuration keys are stable.** Read these and only these from
  configuration:
  - `codedvalue-ai-provider` (`ollama` | `openrouter`)
  - `Ollama:DefaultModel`
  - `OpenRouter:DefaultModel`
  - `OpenRouter:Endpoint`, `OpenRouter:ApiKey`
- **No model or provider defaults in call sites.** Defaults live in
  `ChatModelResolver` (`DefaultOllamaModel`, `DefaultOpenRouterModel`).
- **Streaming errors must surface as `ChatUpdate.Error`, not exceptions.**
  Provider HTTP failures, rate limits, and transport errors are caught inside
  `CodedValueAIService.ChatAsync` and yielded as a structured error update so
  the `/api/ai/chat` endpoint never returns 500 due to a provider hiccup.
- **Keep `ChatModelResolver` pure.** It takes raw config values and returns a
  tuple. It does not depend on `IConfiguration`, `IChatClientFactory`, or any
  DI type — that's what makes it cheap to unit-test and reusable from
  HTTP endpoints, services, and tests alike.

## Secrets handling

- **`OpenRouter:ApiKey` MUST NOT be committed to source control.** Configure
  it via one of:
  - `dotnet user-secrets set OpenRouter:ApiKey "<key>"` (the project has
    `<UserSecretsId>schoolcollab-ai-api</UserSecretsId>` set)
  - Environment variable `OpenRouter__ApiKey` in production / CI
  - Aspire configuration in `AppHost` for local multi-service runs
- `appsettings.json` SHOULD contain a placeholder or omit the key entirely;
  the service already logs a warning and falls back to a no-op client when
  the key is missing.

## Tests

- Unit tests for the AI layer use **Moq** for `IChatClientFactory` and a
  **deterministic `MockChatClient`** (in
  `tests/SchoolCollab.CodedValues.Tests.Unit`) to drive `ChatAsync` round
  trips.
- Live integration tests against the real OpenRouter endpoint live in
  `tests/SchoolCollab.CodedValues.Tests.Integration`
  (`CodedValueAIServiceLiveTests.cs`). They load
  `ai-appsettings.json` for the API key and the configured model.
- Pure-function changes to `ChatModelResolver` must be covered by
  `[DataRow]`-driven tests in `ChatModelResolverTests.cs` covering both
  providers and every defaulting branch.
- `CodedValueAIService` is constructed with five dependencies in the test
  helper — use `ConfigurationBuilder().AddInMemoryCollection(...)` to supply
  `IConfiguration` rather than mocking it.

## When adding a new provider

1. Add the provider name to `ChatClientFactory.Providers`.
2. Wire up the chat client registration in `SchoolCollab.AI/Program.cs` so
   DI can supply it to `ChatClientFactory`.
3. Add the provider's default-model key + default value to `ChatModelResolver`.
4. Add unit tests for the new branches in `ChatModelResolverTests`.
5. Update `appsettings.json` only if the provider requires non-secret config
   (endpoint, default model). Secrets stay out of source-controlled files.