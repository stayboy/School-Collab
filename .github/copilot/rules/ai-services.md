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

### The AI service is a read-only consumer of centralised configuration

- The AI service is hosted in its own process (`SchoolCollab.AI/Program.cs`).
  It binds values for `codedvalue-ai-provider`, `Ollama:Endpoint`,
  `Ollama:DefaultModel`, `OpenRouter:Endpoint`, `OpenRouter:DefaultModel`,
  `OpenRouter:ApiKey` from **standard .NET configuration** — i.e. from any
  configuration source the host's `IConfiguration` knows about.
- **The AppHost owns the source of truth for these values**, declared in
  `src/AppHost/SchoolCollab.AppHost/appsettings.json` under `Parameters:*`
  and fanned out to `coded-values-ai` via `WithEnvironment(...)`. The AI
  host's own `appsettings.json` does not contain these keys; if you need
  to change the active provider, the default model, or an endpoint, edit
  the AppHost's `Parameters:` section — that's the one place every
  developer / operator / CI pipeline needs to know about. See
  `documents/configuration.md` §2 ("Aspire AppHost — shared infrastructure")
  for the canonical table.
- The OpenRouter API key is a **secret parameter** on the AppHost
  (`Parameters:openrouter-api-key`, `secret: true`), kept out of source
  control via the AppHost's user-secrets store
  (`UserSecretsId=71bc1e6c-899e-4131-98f2-60199f7d3ba2`). See
  `documents/configuration.md` §2 for the `dotnet user-secrets` recipe.
- Admin hosts (`SchoolCollab.Admin`, `SchoolCollab.Settings.Application`, etc.)
  etc.) **MUST NOT register `CodedValueAIService`, `IChatClientFactory`,
  `ChatModelResolver`, or `IConfiguration`-based AI resolvers in their DI
  container.** Those types live behind the AI host's HTTP boundary.
- Admin pages that want to display the active provider/model MUST call
  `AiChatClient.GetConfigurationAsync()` (HTTP to `/api/ai/config`), not
  inject the AI service directly. This keeps the modular boundary intact
  and means admin pages work even if the AI host is unreachable (the
  existing `ErrorBoundary` wraps the chat UI).
- `Program.cs` MUST NOT hardcode provider or model strings — they flow
  from the AppHost's centralised `Parameters:*` via `ChatModelResolver`.
- HTTP endpoints inside the AI host MUST NOT accept a model from the
  client. The server resolves the model from configuration inside
  `ChatAsync`.

## Coding Standards

- **Strong typing for AI tool definitions.** Tool metadata (`name`,
  `description`, parameter shapes) lives in `_tools` inside
  `CodedValueAIService` and is wired up via `AIFunctionFactory.Create(...)`.
  Don't construct `AITool` instances by hand unless the call site
  genuinely needs a custom `AIFunction` subclass.
- **Use `Microsoft.Extensions.AI` abstractions** (`IChatClient`,
  `ChatOptions`, `ChatMessage`, `ChatRole`, `TextContent`,
  `FunctionCallContent`, `FunctionResultContent`). Do not pull
  provider-specific types (`OpenAI.Chat.ChatCompletion`, etc.) into
  `SchoolCollab.AI` business logic.
- **Provider-specific SDKs are confined to `ChatClientFactory`.** That
  is the only place `OpenAIClient` / Ollama clients are constructed.
- **Configuration keys are stable.** The AI host reads these and only
  these from configuration:
  - `codedvalue-ai-provider` (`ollama` | `openrouter`)
  - `Ollama:DefaultModel`
  - `OpenRouter:DefaultModel`
  - `OpenRouter:Endpoint`, `OpenRouter:ApiKey`
  The values themselves are sourced from the AppHost's `Parameters:*`
  block via Aspire-injected env vars; see `Architecture` above.
- **No model or provider defaults in call sites.** Defaults live in
  `ChatModelResolver` (`DefaultOllamaModel`, `DefaultOpenRouterModel`).
- **Streaming errors must surface as `ChatUpdate.Error`, not exceptions.**
  Provider HTTP failures, rate limits, and transport errors are caught
  inside `CodedValueAIService.ChatAsync` and yielded as a structured error
  update so the `/api/ai/chat` endpoint never returns 500 due to a
  provider hiccup.
- **Keep `ChatModelResolver` pure.** It takes raw config values and
  returns a tuple. It does not depend on `IConfiguration`,
  `IChatClientFactory`, or any DI type — that's what makes it cheap to
  unit-test and reusable from HTTP endpoints, services, and tests alike.

## Secrets handling

- **`Parameters:openrouter-api-key` MUST NOT be committed to source
  control.** Configure it via one of:
  - `dotnet user-secrets --project src/AppHost/SchoolCollab.AppHost set
    "Parameters:openrouter-api-key" "<key>"` (the AppHost project has
    `<UserSecretsId>71bc1e6c-899e-4131-98f2-60199f7d3ba2</UserSecretsId>`)
  - Environment variable `Parameters__openrouter_api_key` in production /
    CI
- `src/SchoolCollab.AI/appsettings.json` carries neither the API key nor
  any other AI provider values — every input is delivered by the AppHost.
- The service logs a warning and falls back to a no-op client when the
  key is missing.

## Tests

- Unit tests for the AI layer use **Moq** for `IChatClientFactory` and a
  **deterministic `MockChatClient`** (in
  `tests/SchoolCollab.CodedValues.Tests.Unit`) to drive `ChatAsync` round
  trips.
- Live integration tests against the real OpenRouter endpoint live in
  `tests/SchoolCollab.CodedValues.Tests.Integration`
  (`CodedValueAIServiceLiveTests.cs`). They load
  `appHost-appsettings.json` (a link to the AppHost's `appsettings.json`)
  for the endpoint and the configured model, and read the API key from
  the **AppHost's UserSecretsId**
  (`71bc1e6c-899e-4131-98f2-60199f7d3ba2`) via
  `Parameters:openrouter-api-key`. The integration test project pins the
  same UserSecretsId in its own `<AppHostUserSecretsId>` MSBuild
  property; keep them in sync.
- Pure-function changes to `ChatModelResolver` must be covered by
  `[DataRow]`-driven tests in `ChatModelResolverTests.cs` covering both
  providers and every defaulting branch.
- `CodedValueAIService` is constructed with five dependencies in the
  test helper — use `ConfigurationBuilder().AddInMemoryCollection(...)` to
  supply `IConfiguration` rather than mocking it.

## When adding a new provider

1. Add the provider name to `ChatClientFactory.Providers`.
2. Wire up the chat client registration in `SchoolCollab.AI/Program.cs`
   so DI can supply it to `ChatClientFactory`.
3. Add the provider's default-model key + default value to
   `ChatModelResolver`.
4. **Add a `Parameters:ai-<provider>-*` entry to
   `src/AppHost/SchoolCollab.AppHost/appsettings.json`** (endpoint,
   default model, and any URL/key) plus a matching `AddParameter(...)`
   and `WithEnvironment(...)` in `Program.cs`. Update §7 of
   `documents/configuration.md` in the same PR.
5. Add unit tests for the new branches in `ChatModelResolverTests`.
6. Update `documents/configuration.md` only if the provider requires
   non-secret config (endpoint, default model). Secrets stay out of
   source-controlled files and are modelled as
   `AddParameter(..., secret: true)` in the AppHost.

## Trimming the AI system prompt

When you need to reduce the size of the system prompt, follow the
pattern in `documents/ai-prompts/README.md` ("Pattern for trimming
prompts"). The short version:

- Snapshot the original as `ai-system-prompt.original.md` next to the
  active prompt.
- Update the loader's fallback chain (see
  `CodedValueAIService.GetSystemPrompt`) to prefer the trimmed file
  first and fall back to the original — single-step rollback by
  deleting the trimmed copy.
- Pair the prompt trim with a per-prompt tool filter
  (`CodedValueAIService.SelectToolsForPrompt`); together they cut
  more input tokens than either alone.
- Add unit tests for the tool filter
  (`CodedValueAIServiceToolSelectionTests`) and verify against a
  live-model probe before committing.
- Save the trimmed variant to `documents/ai-prompts/` so future
  agents have a reference archive.
