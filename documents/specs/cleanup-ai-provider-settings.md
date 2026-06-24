# Cleanup AI Provider Settings

## 1. Title and Metadata

- **Title:** Cleanup AI provider and default-model configuration
- **Author:** AI Coding Assistant
- **Date:** 2026-06-23
- **Status:** Draft
- **Reviewers:** TBD

## 2. Context

The codebase currently stores AI provider connection details in top-level `Ollama` and `OpenRouter` configuration sections. A previous change also introduced an `AI` section containing `DefaultProvider`, `DefaultModel`, and a full `Models` list used by the Coded Values AI chat component.

This design is duplicated and confusing:
- `Ollama:Model` and `OpenRouter:DefaultModel` are provider-specific default models.
- `AI:DefaultModel` is a second default model that is not clearly scoped to the Coded Values chat.
- `AI:Models` is a UI list that is no longer needed because the model is selected from configuration, not the UI.
- There is no single key that explicitly selects which provider the Coded Values AI chat should use.

This spec consolidates the configuration so that the Coded Values AI chat uses a single, explicit provider key and reads the default model from that provider's own settings.

## 3. Functional Requirements

FR-1. **Single provider selector:** The system MUST expose a configuration key `codedvalue-ai-provider` whose value is either `ollama` or `openrouter`. This key selects which provider the Coded Values AI chat uses.

FR-2. **Per-provider default model:** The `Ollama` section MUST contain a `Model` key that is the default model when `codedvalue-ai-provider` is `ollama`.

FR-3. **Per-provider default model:** The `OpenRouter` section MUST contain a `DefaultModel` key that is the default model when `codedvalue-ai-provider` is `openrouter`.

FR-4. **Remove redundant `AI` section:** The host `appsettings.json` file MUST NOT contain an `AI` section. Any `AI` configuration that is still required by existing services must be replaced by the new `codedvalue-ai-provider` and provider sections.

FR-5. **Chat component reads from provider config:** The Coded Values AI chat component MUST determine the effective model by reading:
  1. `codedvalue-ai-provider`
  2. The corresponding provider section (`Ollama:Model` or `OpenRouter:DefaultModel`)

FR-6. **Fallback behavior:** If `codedvalue-ai-provider` is missing, empty, or an unknown value, the system MUST fall back to the `ollama` provider and use `Ollama:Model` (defaulting to `llama3.1:8b` if that key is also missing).

FR-7. **No UI model selection:** The Coded Values landing page and chat component MUST NOT render a model-selection dropdown. The selected model is controlled exclusively by configuration.

FR-8. **Configuration reload support:** The chat component SHOULD re-read the configuration on initialization so that changes to `appsettings` take effect without recompiling. (Runtime reload via `IOptionsSnapshot` is not required for this cleanup.)

FR-9. **Remove obsolete `AiOptions` and `AiModelInfo` classes:** The `SchoolCollab.Admin.Shared.Options.AiOptions` and `AiModelInfo` classes MUST be removed because they are no longer used after the `AI` section is removed.

FR-10. **Update registration in host:** The host `Program.cs` MUST remove any binding or registration of `AiOptions`.

## 4. Non-Functional Requirements

NFR-1. **Backward compatibility:** Existing `Ollama:Endpoint` and `OpenRouter:Endpoint` / `OpenRouter:ApiKey` settings MUST remain unchanged because the underlying `IChatClient` registrations depend on them.

NFR-2. **Build health:** The solution MUST build with zero errors after the cleanup.

NFR-3. **Tests:** All existing unit tests MUST continue to pass. Obsolete tests that reference the removed `AI` model resolution logic MUST be removed.

NFR-4. **Clarity:** A developer reading `appsettings.json` MUST be able to determine the active provider and default model in two lines.

## 5. Acceptance Criteria

AC-1. **Given** a host `appsettings.json` with `codedvalue-ai-provider` set to `ollama`, **when** the Coded Values AI chat initializes, **then** it uses `Ollama:Model` as the effective model.

AC-2. **Given** a host `appsettings.json` with `codedvalue-ai-provider` set to `openrouter`, **when** the Coded Values AI chat initializes, **then** it uses `OpenRouter:DefaultModel` as the effective model.

AC-3. **Given** a host `appsettings.json` without `codedvalue-ai-provider`, **when** the Coded Values AI chat initializes, **then** it defaults to the `ollama` provider and `llama3.1:8b`.

AC-4. **Given** the Coded Values landing page is rendered, **when** the user inspects the page, **then** no model-selection dropdown is visible.

AC-5. **Given** the solution is built, **when** the build completes, **then** no errors or warnings related to `AiOptions` or `AiModelInfo` are produced.

AC-6. **Given** the unit test suite runs, **when** the run completes, **then** all tests pass and no tests reference the removed `AI` model resolution logic.

## 6. Edge Cases

EC-1. `codedvalue-ai-provider` is uppercase (`OLLAMA`). The system MUST normalize the value case-insensitively.

EC-2. `codedvalue-ai-provider` is an unknown value (e.g. `azure`). The system MUST fall back to `ollama`.

EC-3. `Ollama:Model` is missing. The system MUST fall back to `llama3.1:8b`.

EC-4. `OpenRouter:DefaultModel` is missing when provider is `openrouter`. The system MUST fall back to `openai/gpt-4o-mini`.

EC-5. `codedvalue-ai-provider` is whitespace-only. The system MUST treat it as missing and fall back to `ollama`.

## 7. API Contracts

N/A — this is a configuration cleanup with no external API changes. The internal `AiChatClient.ChatAsync` signature remains unchanged; only the model argument passed to it changes.

## 8. Data Models

### Configuration schema

```json
{
  "codedvalue-ai-provider": "ollama",
  "Ollama": {
    "Endpoint": "http://localhost:11434/v1",
    "Model": "llama3.1:8b"
  },
  "OpenRouter": {
    "Endpoint": "https://openrouter.ai/api/v1",
    "ApiKey": "...",
    "DefaultModel": "openai/gpt-4o-mini"
  }
}
```

### Removed configuration schema

```json
{
  "AI": {
    "DefaultProvider": "ollama",
    "DefaultModel": "llama3.1:8b",
    "Models": [ ... ]
  }
}
```

## 9. Out of Scope

OS-1. Adding runtime UI for switching providers or models. The selection is configuration-only.

OS-2. Changing how `IChatClient` instances are registered in `SchoolCollab.AI`. The existing `Ollama` and `OpenRouter` registrations remain in place.

OS-3. Adding per-tenant or per-user provider/model overrides.

OS-4. Secrets management (e.g. moving `OpenRouter:ApiKey` to Key Vault).

## 10. Implementation Plan

IP-1. Update `src/SchoolCollab.Admin/appsettings.json`:
   - Remove the `AI` section.
   - Add `codedvalue-ai-provider` at the top level.
   - Keep `Ollama` and `OpenRouter` sections.

IP-2. Update `src/SchoolCollab.AI/appsettings.json`:
   - Remove the `AI` section.
   - Add `codedvalue-ai-provider` at the top level.
   - Keep `Ollama` and `OpenRouter` sections.

IP-3. Update `src/CodedValues/SchoolCollab.CodedValues.Admin/appsettings.json`:
   - Remove the `AI` section.
   - Add `codedvalue-ai-provider` at the top level.
   - Keep `Ollama` section as fallback.

IP-4. Remove `src/SchoolCollab.Admin.Shared/Options/AiOptions.cs` and `AiModelInfo`.

IP-5. Update `src/SchoolCollab.Admin/Program.cs`:
   - Remove `builder.Services.Configure<AiOptions>(...)` registration.

IP-6. Update `src/CodedValues/SchoolCollab.CodedValues.Admin/Components/Pages/CodedValues/CodedValuesChat.razor`:
   - Remove `IOptions<AiOptions>` injection.
   - Inject `IConfiguration` (or continue using `IOptions` if replaced by a new provider-specific options type).
   - Resolve the effective model by reading `codedvalue-ai-provider` and the corresponding provider section.
   - Remove the `SelectedModel` parameter if still present.

IP-7. Update any `_Imports.razor` files to remove unused `SchoolCollab.Admin.Shared.Options` and `Microsoft.Extensions.Options` references if they become unused.

IP-8. Run `dotnet build` and unit tests. Remove any obsolete tests referencing the old `AI` model resolution.

## 11. Risks

R-1. If other admin modules reference `AiOptions`, removing the class will break compilation. Mitigation: search the solution before removal.

R-2. The `SchoolCollab.AI` service currently uses `AI:DefaultProvider` to decide the active provider via `ChatClientFactory`. Replacing that with `codedvalue-ai-provider` may require a coordinated change in `SchoolCollab.AI` if it is intended to use the same key. Mitigation: keep `SchoolCollab.AI` using its existing provider resolution for now; this cleanup targets the Coded Values chat's model selection.
