# Reusable AI Chat RCL — Implementation Plan

## 1. Goal

Extract the AI chat surface (Blazor component, drawer panel, mirror hub, HTTP client, JS keyboard handler, CSS) out of the CodedValues-specific landing page into a domain-agnostic RCL so any admin landing page can host the same AI assistant with a different system prompt and a different set of tool calls. Genericise the `SchoolCollab.AI` server so the CodedValues tool bag becomes one of many pluggable tool providers.

`SchoolCollab.AI` is the existing CodedValues-flavoured reference project. After this refactor it stays a working example (CodedValues remains the first tool provider plugged into the generic engine), but new bounded contexts can ship their own provider without forking the chat UI, the drawer, the SSE protocol, the JS keyboard handler, the scoped mirror hub, or the HTTP transport.

## 2. Decisions (proposed, to be reviewed)

| Topic | Decision |
|-------|----------|
| New RCL | `src/SchoolCollab.AI.Chat/SchoolCollab.AI.Chat.csproj` (`Microsoft.NET.Sdk.Razor`) |
| Generic server | Rename `src/SchoolCollab.AI/` → `src/SchoolCollab.AI.Server/` and remove its dependency on `SchoolCollab.Settings.Contracts` |
| CodedValues tool provider | New project `src/SchoolCollab.AI.Tools.CodedValues/` consumed by `SchoolCollab.AI.Server` (and only it) |
| System prompt | A `ISystemPromptProvider` plug-in interface; the CodedValues provider reads `Prompts/ai-system-prompt.md` as today |
| Tool bag | `IToolProvider` plug-in interface; the CodedValues provider exposes the 9 existing tools |
| Mirroring hub | Rename `CodedValuesChatHub` → `AiChatHub`; move to `SchoolCollab.AI.Chat/Services/` |
| Chat component | Rename `CodedValuesChat` → `AiChat`; move to `SchoolCollab.AI.Chat/Components/`; new namespace `SchoolCollab.AI.Chat.Components` |
| Drawer panel | Rename `CodedValuesChatPanel` → `AiChatPanel`; move to `SchoolCollab.AI.Chat/Components/`; uses `SideDrawer` from `SchoolCollab.Admin.Shared` |
| HTTP client | `AiChatClient` moves from `SchoolCollab.Admin.Shared/Services/` → `SchoolCollab.AI.Chat/Services/`; `Admin.Shared` keeps no AI dependency |
| Models & protocol | `ChatUpdate`, `ChatRequest`, `ChatMessageRequest`, `AiProgramMarker` move to a new `SchoolCollab.AI.Abstractions` class library so both `SchoolCollab.AI.Chat` (client) and `SchoolCollab.AI.Server` (host) can reference them without circularity |
| Chat engine | `CodedValueAIService` becomes `AIChatEngine` in `SchoolCollab.AI.Server`; takes `IEnumerable<IToolProvider>` + `ISystemPromptProvider` via DI; per-turn tool filtering is preserved inside each provider (the CodedValues provider keeps the existing `SelectToolsForPrompt(...)` intent classifier — see §6) |
| Post-create navigation | `CodedValuesChat`'s hard-coded `Nav.NavigateTo("/coded-values/...")` becomes `EventCallback<ToolCallDisplay> OnToolCallCompleted`; CodedValues-specific behaviour moves to a thin consumer-side handler in the Settings landing page |
| Feature flag | `FEATURE:EnableCodedValuesAiChat` stays in `Settings.FeatureFlag`; the chat's enable/disable gating is now per-host via `[Parameter] public bool Enabled` (default `true`) so each landing page can opt in/out with its own flag |
| RCL `_Imports.razor` | New RCL ships its own `Components/_Imports.razor` so consumer pages only need a single `@using SchoolCollab.AI.Chat.Components` |
| CSS | All chat CSS lives in the RCL (`SchoolCollab.AI.Chat/Components/AiChat.razor.css`, `AiChatPanel.razor.css`); consumers get it for free via Blazor scoped CSS bundling |
| JS | `chatInput.js` moves to the RCL's `wwwroot/js/`; the import path in the component changes from `./_content/SchoolCollab.Settings.Admin/js/chatInput.js` to `./_content/SchoolCollab.AI.Chat/js/chatInput.js` |

## 3. New project tree

```
src/
├── SchoolCollab.AI.Abstractions/        # netstandard2.1 or net10 class lib
│   ├── ChatUpdate.cs                    # moved from SchoolCollab.AI
│   ├── ChatEndpointTypes.cs             # moved as a single file from SchoolCollab.AI (contains AiProgramMarker, ChatRequest, ChatMessageRequest)
│   ├── IToolProvider.cs                 # NEW
│   ├── ISystemPromptProvider.cs         # NEW
│   ├── ToolDescriptor.cs                # NEW (name, friendlyName, description, argsSummary pattern)
│   └── SchoolCollab.AI.Abstractions.csproj
│
├── SchoolCollab.AI.Chat/                # Microsoft.NET.Sdk.Razor RCL
│   ├── Components/
│   │   ├── AiChat.razor                 # moved + genericised from CodedValuesChat
│   │   ├── AiChat.razor.css             # moved from Settings.Admin
│   │   ├── AiChatPanel.razor            # moved + genericised from CodedValuesChatPanel
│   │   ├── AiChatPanel.razor.css        # moved from Settings.Admin
│   │   ├── _Imports.razor               # NEW
│   │   └── AiChatMode.cs                # public enum (Full/DisplayOnly/InputOnly)
│   ├── Services/
│   │   ├── AiChatClient.cs              # moved from Admin.Shared
│   │   ├── AiChatHub.cs                 # moved + renamed from CodedValuesChatHub
│   │   └── AiChatModule.cs              # NEW: IChatFeatureFlagService, IConversationStore, etc.
│   ├── wwwroot/
│   │   └── js/chatInput.js              # moved from Settings.Admin
│   └── SchoolCollab.AI.Chat.csproj
│
├── SchoolCollab.AI.Server/              # Microsoft.NET.Sdk.Web — was SchoolCollab.AI
│   ├── Program.cs                       # generic endpoint registration
│   ├── Services/
│   │   ├── AIChatEngine.cs              # was CodedValueAIService, generic
│   │   ├── ChatClientFactory.cs         # moved as-is
│   │   ├── IChatClientFactory.cs        # moved as-is
│   │   ├── ChatModelResolver.cs         # moved as-is
│   │   └── AiTextCleaner.cs             # moved as-is
│   ├── Prompts/
│   │   └── default-system-prompt.md     # moved from Prompts/ai-system-prompt.md
│   └── SchoolCollab.AI.Server.csproj
│
└── SchoolCollab.AI.Tools.CodedValues/   # net10 class lib — CodedValues tool provider
    ├── CodedValuesToolProvider.cs       # implements IToolProvider (keeps the existing SelectToolsForPrompt intent classifier)
    ├── CodedValuesApiClient.cs          # moved as-is (declares both ICodedValuesApiClient + CodedValuesApiClient in one file, as today)
    ├── BulkChildItem.cs                 # moved as-is
    ├── Prompts/
    │   └── coded-values-system-prompt.md  # moved as-is
    └── SchoolCollab.AI.Tools.CodedValues.csproj
```

`SchoolCollab.Admin.Shared` loses its `SchoolCollab.AI` and `SchoolCollab.AI.Chat` references. Note there are **two distinct `CodedValuesApiClient` types** today: `SchoolCollab.Admin.Shared.Services.CodedValuesApiClient` (the typed HTTP client the Admin UI uses for dropdowns, pointed at `settings-api`) and `SchoolCollab.AI.Services.CodedValuesApiClient` (the one the AI tool dispatch uses, with its `ICodedValuesApiClient`). They are unrelated types with duplicate DTOs. This refactor moves only the **AI** one into `SchoolCollab.AI.Tools.CodedValues`; `Admin.Shared` keeps its own `CodedValuesApiClient` for dropdowns untouched and gains **no** reference to `SchoolCollab.AI.Tools.CodedValues`. Consolidating the duplicate DTOs/clients is a separate follow-up (see OS-9). The `AiChatClient` reference in `Admin.Shared` is removed.

## 4. Namespace mapping

| Old | New |
|-----|-----|
| `SchoolCollab.AI.ChatUpdate` | `SchoolCollab.AI.Abstractions.ChatUpdate` |
| `SchoolCollab.AI.ChatRequest` | `SchoolCollab.AI.Abstractions.ChatRequest` |
| `SchoolCollab.AI.ChatMessageRequest` | `SchoolCollab.AI.Abstractions.ChatMessageRequest` |
| `SchoolCollab.AI.AiProgramMarker` | `SchoolCollab.AI.Abstractions.AiProgramMarker` |
| `SchoolCollab.AI.Services.CodedValueAIService` | `SchoolCollab.AI.Server.Services.AIChatEngine` |
| `SchoolCollab.AI.Services.ChatClientFactory` | `SchoolCollab.AI.Server.Services.ChatClientFactory` |
| `SchoolCollab.AI.Services.IChatClientFactory` | `SchoolCollab.AI.Server.Services.IChatClientFactory` |
| `SchoolCollab.AI.Services.ChatModelResolver` | `SchoolCollab.AI.Server.Services.ChatModelResolver` |
| `SchoolCollab.AI.Services.AiTextCleaner` | `SchoolCollab.AI.Server.Services.AiTextCleaner` |
| `SchoolCollab.AI.Services.ICodedValuesApiClient` | `SchoolCollab.AI.Tools.CodedValues.ICodedValuesApiClient` |
| `SchoolCollab.AI.Services.CodedValuesApiClient` | `SchoolCollab.AI.Tools.CodedValues.CodedValuesApiClient` |
| `SchoolCollab.AI.Services.BulkChildItem` | `SchoolCollab.AI.Tools.CodedValues.BulkChildItem` |
| `SchoolCollab.Admin.Shared.Services.AiChatClient` | `SchoolCollab.AI.Chat.Services.AiChatClient` |
| `SchoolCollab.Settings.Admin.Services.CodedValuesChatHub` | `SchoolCollab.AI.Chat.Services.AiChatHub` |
| `SchoolCollab.Settings.Admin.Components.Pages.CodedValues.CodedValuesChat` | `SchoolCollab.AI.Chat.Components.AiChat` |
| `SchoolCollab.Settings.Admin.Components.Pages.CodedValues.CodedValuesChatMode` | `SchoolCollab.AI.Chat.Components.AiChatMode` |
| `SchoolCollab.Settings.Admin.Components.Pages.CodedValues.CodedValuesChatPanel` | `SchoolCollab.AI.Chat.Components.AiChatPanel` |

## 5. Public API contract for the RCL

### `AiChat` component (`SchoolCollab.AI.Chat.Components.AiChat`)

Replaces the current `CodedValuesChat`. Public surface:

```text
public enum AiChatMode { Full, DisplayOnly, InputOnly }

public record ToolCallDisplay(string CallId, string FriendlyName, string ArgsSummary,
    string? ResultSummary = null, bool? Success = null);

public record AiChatMessage(ChatRole Role, string Text, List<ToolCallDisplay>? ToolCalls = null);

public record AiChatStreamingState(bool IsStreaming, string StreamingText,
    IReadOnlyList<ToolCallDisplay>? ActiveToolCalls);

[Parameter] AiChatMode Mode { get; set; } = AiChatMode.Full;
[Parameter] bool HideHeader { get; set; }
[Parameter] bool ShowIntro { get; set; }
[Parameter] bool SuppressResponseDisplay { get; set; }
[Parameter] bool Enabled { get; set; } = true;
[Parameter] string? Title { get; set; } = "✨ AI Assistant";
[Parameter] string? Intro { get; set; }
[Parameter] string? Placeholder { get; set; } = "Ask me anything...";
[Parameter] string? Prompt { get; set; }
[Parameter] IReadOnlyList<AiChatMessage>? ExternalMessages { get; set; }
[Parameter] AiChatStreamingState? ExternalStreamingState { get; set; }
[Parameter] EventCallback<AiChatMessage> OnMessageAdded { get; set; }
[Parameter] EventCallback OnCleared { get; set; }
[Parameter] EventCallback<AiChatStreamingState> OnStreamingStateChanged { get; set; }
[Parameter] EventCallback OnPromptSending { get; set; }
[Parameter] EventCallback OnPromptSent { get; set; }
[Parameter] EventCallback<string> OnPromptSubmitted { get; set; }
[Parameter] EventCallback<ToolCallDisplay> OnToolCallCompleted { get; set; }
```

Behaviour changes from `CodedValuesChat`:

- **No more hard-coded `Nav.NavigateTo`** — the bulk-create navigation becomes `OnToolCallCompleted(bulkCall)`. The CodedValues landing page supplies the handler that does the navigation; other consumers ignore the callback or wire their own behaviour.
- **No more `Create Bulk Values` filtering** — the chat only fires the event; the consumer decides what `FriendlyName` strings matter.
- **`Title` and `Intro` are parameters** instead of being hard-coded to "✨ AI Assistant" and the CodedValues intro paragraph. The defaults are the same string the current chat uses, so the CodedValues landing page needs no change beyond the rename.
- **`Enabled` gates the whole chat** — when `false` the component renders nothing (or a polite "AI assistant is disabled" `FluentMessageBar` if `Title` is also supplied; the default is to render nothing). This replaces the current `if (_aiChatEnabled == true)` checks at call sites with a single per-instance parameter.
- **`Placeholder` parameter** — defaults to a generic "Ask me anything..."; the CodedValues landing page passes the current "Ask me to create coded values..." string.

### `AiChatPanel` component (`SchoolCollab.AI.Chat.Components.AiChatPanel`)

Replaces `CodedValuesChatPanel`. Wraps `SideDrawer` from `SchoolCollab.Admin.Shared`. This is **not** a pure rename: the current `CodedValuesChatPanel` exposes only 3 parameters (`Open`, `Prompt`, `OpenChanged`) and hard-codes `Title="✨ AI Assistant"`, `Width="420px"`, `CancelText="Close"` as literals on the inner `<SideDrawer>`. The RCL promotes those literals to `[Parameter]`s and adds `Enabled`, so the panel gains 4 new parameters. Public surface:

```text
[Parameter] bool Open { get; set; }
[Parameter] EventCallback<bool> OpenChanged { get; set; }
[Parameter] string? Prompt { get; set; }
[Parameter] string? Title { get; set; } = "✨ AI Assistant";      // NEW (was a hard-coded literal on <SideDrawer>)
[Parameter] string? Width { get; set; } = "420px";              // NEW (was a hard-coded literal)
[Parameter] string? CancelText { get; set; } = "Close";         // NEW (was a hard-coded literal)
[Parameter] bool Enabled { get; set; } = true;                 // NEW (forwarded to the inner AiChat)
```

**Forwarding to the inner `AiChat`:** the panel hosts an `AiChat` in `Full` mode. It forwards `Title`, `Enabled`, `OnToolCallCompleted` (see below) and `Prompt` to the inner chat, and keeps `HideHeader="true"` and `ShowIntro="true"` hard-coded internally (matching today's panel). `Intro` and `Placeholder` are *not* surfaced on the panel in the first PR — the inner chat uses its own defaults — but can be added later. `OnToolCallCompleted` bubbles up so a host that opens the drawer (not the inline bar) still receives bulk-create navigation events. The mirroring plumbing (`ExternalMessages` / `ExternalStreamingState` fed from the shared `AiChatHub`) stays internal to the panel — it is not part of the public surface.

### `AiChatHub` service (`SchoolCollab.AI.Chat.Services.AiChatHub`)

Scoped bridge. Same shape as `CodedValuesChatHub`, with `AiChatMessage` / `AiChatStreamingState` types:

```text
public sealed class AiChatHub
{
    public IReadOnlyList<AiChatMessage> Messages { get; }
    public AiChatStreamingState StreamingState { get; }
    public event Action? Changed;
    public void AddMessage(AiChatMessage message);
    public void SetStreamingState(AiChatStreamingState state);
    public void Clear();
}
```

### `AiChatClient` (`SchoolCollab.AI.Chat.Services.AiChatClient`)

Same shape as the current `SchoolCollab.Admin.Shared.Services.AiChatClient`. No public-API changes — only the namespace move.

### `SchoolCollab.AI.Chat/Services/AiChatModule.cs`

```text
public static class AiChatModule
{
    public static IServiceCollection AddAiChat(this IServiceCollection services, Action<HttpClient> configure)
    {
        services.AddHttpClient<AiChatClient>(configure);
        services.AddScoped<AiChatHub>();
        return services;
    }
}
```

Call sites become `builder.Services.AddAiChat(c => c.BaseAddress = new Uri("https+http://settings-ai"));` instead of the manual `AddHttpClient<AiChatClient>(...)` block in `ModuleServices.cs`. The `settings-ai` address above is the CodedValues example; the method is domain-agnostic — each host supplies the base address of *its own* AI server.

## 6. Server-side `IToolProvider` / `ISystemPromptProvider` contract

```text
namespace SchoolCollab.AI.Abstractions;

/// <summary>
/// A bag of tools the AI engine exposes to the model. One provider per
/// bounded context (e.g. CodedValues, Assignments, Students). The engine
/// aggregates all registered providers and dispatches tool calls by name.
/// </summary>
public interface IToolProvider
{
    /// <summary>Stable, namespaced tool names (e.g. "coded_values.create_bulk").</summary>
    IReadOnlyList<string> ToolNames { get; }

    /// <summary>Build the AITool list for the current turn. The provider may
    /// narrow the list per turn (e.g. the CodedValues provider applies its
    /// SelectToolsForPrompt intent classifier here). history is the current
    /// chat message list so the provider can classify intent.</summary>
    IReadOnlyList<AITool> CreateTools(IReadOnlyList<ChatMessage> history, ILogger logger);

    /// <summary>Route a tool call to the right local implementation.</summary>
    Task<string> DispatchAsync(string toolName, string? args, CancellationToken ct);
}

// Note: no IHttpContextAccessor on CreateTools. The current CodedValueAIService
// is a singleton that holds an injected ICodedValuesApiClient and never reads
// per-request HTTP context at tool-creation time. If a future provider needs
// per-request claims/tenant to build its AITools, it can resolve a scoped
// service inside DispatchAsync; do not bake the accessor into the contract.

/// <summary>
/// Source of the system prompt + per-turn framing. The engine reads this
/// once per chat turn and prepends it to the message history.
/// </summary>
public interface ISystemPromptProvider
{
    Task<string> GetSystemPromptAsync(CancellationToken ct);
    /// <summary>True if the engine should include the current tool list in the
    /// framing message — useful for letting the model see the live tool bag.</summary>
    bool IncludesToolList { get; }
}
```

The CodedValues provider implements both:

- `CodedValuesToolProvider : IToolProvider` — wraps the current 9 `AIFunctionFactory.Create(...)` calls and the `Dispatch*` switch.
- `CodedValuesSystemPromptProvider : ISystemPromptProvider` — loads `Prompts/coded-values-system-prompt.md` from embedded resources (same as today).

The engine:

- `AIChatEngine` takes `IEnumerable<IToolProvider>`, `ISystemPromptProvider`, `IChatClientFactory`, and `ILogger` via DI.
- For each turn, it concatenates `IReadOnlyList<AITool>` from every provider and prepends the system prompt. Per-turn tool narrowing is **preserved**, not removed: each `IToolProvider.CreateTools(...)` is free to apply its own intent filter before returning its `AITool` list. The CodedValues provider keeps the existing `CodedValueAIService.SelectToolsForPrompt(IReadOnlyList<ChatMessage> history)` classifier verbatim (the keyword→tool-subset logic backed by `_toolsByName` and `FriendlyToolNames`), so the model sees the same filtered tool bag per turn it sees today. The engine itself does not impose a global `toolFilter` callback in the first PR — "include everything each provider returns" — because each provider already owns its own narrowing. A cross-provider `toolFilter(string)` callback can land later (see OS-5).

## 7. Endpoint contract

`/api/ai/chat` request and response shapes do **not** change. The server returns the same `TextChunk` / `ToolCallStart` / `ToolCallEnd` / `Error` SSE events as today. The only difference is the body of the request — the server still receives a `ChatRequest` with `Messages`, but the tool bag is now derived server-side from registered `IToolProvider`s rather than hard-coded.

A new `POST /api/ai/tools` endpoint (optional, gated by NFR-2 below) returns the list of currently registered tool names and their `friendlyName` mappings so admin clients can display a tool-call summary without a separate API per provider. Out of scope for the first PR; see OS-2.

## 8. Functional requirements

- **FR-1** The RCL MUST compile and ship a working `AiChat` component that any admin landing page can drop in with one `<AiChat Mode="AiChatMode.InputOnly" ... />` line.
- **FR-2** The RCL MUST ship a working `AiChatPanel` drawer that hosts a `AiChat` in `Full` mode, mirrored through `AiChatHub`, ready to be opened/closed by the host.
- **FR-3** The RCL MUST depend only on `SchoolCollab.AI.Abstractions` and `SchoolCollab.Admin.Shared` (for `SideDrawer`, `LandingPage`, `SchoolCollabLayout`, FluentUI). It MUST NOT depend on `SchoolCollab.AI.Server` or `SchoolCollab.AI.Tools.CodedValues`.
- **FR-4** The RCL MUST ship the JS keyboard handler (`Enter` to submit, `Shift+Enter` to newline, `ArrowUp`/`ArrowDown` for history navigation) and its CSS.
- **FR-5** The RCL MUST ship a scoped `AiChatModule.AddAiChat(...)` extension that wires `AiChatClient` (HttpClient) + `AiChatHub` (scoped) in one call.
- **FR-6** The `AiChat` component MUST accept a `Title` and `Intro` parameter so different landing pages can change them without forking the component.
- **FR-7** The `AiChat` component MUST accept an `Enabled` parameter that suppresses all rendering (no input, no panel trigger) when `false`.
- **FR-8** The `AiChat` component MUST NOT hard-code any `Nav.NavigateTo` route. Successful tool completions MUST be raised as `OnToolCallCompleted` events for the host to handle.
- **FR-9** `SchoolCollab.AI.Server` MUST start as a generic chat engine that loads tools from `IEnumerable<IToolProvider>` and a system prompt from `ISystemPromptProvider`.
- **FR-10** `SchoolCollab.AI.Tools.CodedValues` MUST ship the existing 9 CodedValues tools, the existing CodedValues system prompt, and the existing AI-side `CodedValuesApiClient` (+ `ICodedValuesApiClient`). Wiring it in MUST restore the current CodedValues chat behaviour byte-for-byte (same tool calls, same prompt, same SSE events, same routing), **including the per-turn tool subset produced by the existing `SelectToolsForPrompt(...)` intent classifier** — the provider MUST apply that classifier inside its `CreateTools(...)` so the model sees the same filtered tool bag per turn it sees today.
- **FR-11** The CodedValues landing page (`Settings.Admin/Components/Pages/CodedValues/Index.razor`) MUST render and behave identically after the rename + extraction. The only behavioural addition is a new handler for `OnToolCallCompleted` that replaces the now-removed in-component `Nav.NavigateTo` call.
- **FR-12** The new projects MUST each have a `.csproj` of the right SDK (`Microsoft.NET.Sdk.Razor` for the RCL, `Microsoft.NET.Sdk.Web` for the server, `Microsoft.NET.Sdk` for the abstractions + tools libraries).
- **FR-13** The sln file MUST be updated so the new projects are listed and the old `SchoolCollab.AI` project is renamed to `SchoolCollab.AI.Server`.
- **FR-14** All existing bUnit + Playwright tests that exercise the CodedValues chat MUST pass after the refactor. The test framework is **MSTest** (`[TestMethod]` / `[DataRow]`), with bUnit driving the Blazor component tests; references to "xUnit/`[Fact]`" are inaccurate. Tests are updated for the namespace/rename only, **except** `CodedValueAIServiceToolSelectionTests.cs`, which asserts on `SelectToolsForPrompt`'s filtering. Because that classifier moves *into* `CodedValuesToolProvider` (FR-10), that test is rewritten to call `CodedValuesToolProvider.CreateTools(history, logger)` instead of `CodedValueAIService.SelectToolsForPrompt(history)` — its assertions stay the same, only the target changes. No test is deleted; this one test is explicitly rewritten (carved out of the "namespace-only" rule).
- **FR-15** The migration MUST be done in named phases (see §10) so each phase ends with a green build + a green test run.

## 9. Non-functional requirements

- **NFR-1 Build** `dotnet build SchoolCollab.sln` MUST succeed with 0 warnings and 0 errors after every phase.
- **NFR-2 Tests** `dotnet test` on `SchoolCollab.Settings.Tests.Unit` MUST keep the same passing test count after every phase. The literal "319" figure in earlier drafts is **stale and unverified** — at refactor start, capture the real baseline with `dotnet test tests/SchoolCollab.Settings.Tests.Unit --logger trx` and treat that count (`total`/`passed`) as the contract. No test may be deleted; `CodedValueAIServiceToolSelectionTests.cs` is rewritten per FR-14 (target changes, assertions preserved).
- **NFR-3 Backward compatibility** The SSE protocol emitted by `POST /api/ai/chat` MUST NOT change. Existing `AiChatClient.ParseSseEvent` (after the move) MUST work with the new server without any client-side change other than the namespace.
- **NFR-4 Bundle** The RCL's CSS and JS MUST be served via the standard Blazor static-asset pipeline (`_content/SchoolCollab.AI.Chat/...`) so consumers don't need to manually register them.
- **NFR-5 Zero regression in CodedValues UX** The CodedValues landing page's chat (input bar at the bottom, drawer on the right with mirrored conversation, "Create Bulk Values" tool call followed by navigation to `/coded-values/{parentCode}/children`) MUST be visually and behaviourally identical after the refactor.
- **NFR-6 Feature flag parity** The current `FEATURE:EnableCodedValuesAiChat` runtime flag MUST continue to gate the CodedValues chat. The landing page's existing `if (_aiChatEnabled == true)` block is preserved and passes the resolved value into `AiChat`'s `Enabled` parameter.
- **NFR-7 Documentation** A new spec document `documents/solution/ai-chat-rcl.md` MUST be added describing the RCL's public surface and a worked example of adding a second tool provider (e.g. a stub `Students` provider).
- **NFR-8 Tooling** The renamed server MUST appear in the Aspire AppHost as the `settings-ai` resource (the `AddProject<Projects.SchoolCollab_AI_Server>("settings-ai")` generic argument changes; the published resource name stays `settings-ai`). The three class libraries (`SchoolCollab.AI.Abstractions`, `SchoolCollab.AI.Chat`, `SchoolCollab.AI.Tools.CodedValues`) are **not** Aspire resources — they are transitive project references consumed by the server / admin host and do not show up on the dashboard. Aspire parameters are unchanged.

## 10. Acceptance criteria

- **AC-1** Given a fresh checkout, when `dotnet build SchoolCollab.sln` is run, then it succeeds with 0 warnings and 0 errors, and the build output includes `SchoolCollab.AI.Abstractions.dll`, `SchoolCollab.AI.Chat.dll`, `SchoolCollab.AI.Server.dll`, and `SchoolCollab.AI.Tools.CodedValues.dll`.
- **AC-2** Given the CodedValues landing page is loaded, when the user types "Create a Country coded value with code CNTRY" and presses Enter, then the AI assistant streams back a proposal and the user sees a confirmation prompt — identical to the current behaviour.
- **AC-3** Given the user has just submitted a bulk-create confirmation, when the AI returns a successful `Create Bulk Values` tool call, then the browser navigates to `/coded-values/CNTRY/children` (regression — this is what the in-component `Nav.NavigateTo` did before the refactor, now driven by the consumer's `OnToolCallCompleted` handler).
- **AC-4** Given the CodedValues chat is mid-stream, when the user presses `ArrowUp` in the input, then the previous prompt is restored into the input box (regression — JS keyboard handler is intact).
- **AC-5** Given the CodedValues landing page is loaded, when the `FEATURE:EnableCodedValuesAiChat` runtime flag is off, then the `✨ Chat` toolbar button, the inline input bar, and the side drawer are all hidden (regression — the existing `_aiChatEnabled` `@if` gate in `Index.razor` still hides the button + drawer; the value is *also* passed to `AiChat`'s `Enabled` parameter so the component internals are suppressed). Note `Enabled` governs only the AiChat/AiChatPanel internals — the toolbar button and drawer are owned by the host page and stay gated by its own `@if (_aiChatEnabled == true)`.
- **AC-6** Given a test renders `AiChat` in `DisplayOnly` mode, when the test inspects the rendered tree, then the input area is not present (regression — `DisplayOnly` still hides the input).
- **AC-7** Given a test renders `AiChat` in `Full` mode and types "Hello", when the test calls `SubmitFromKeyAsync`, then the chat sends the prompt to the AI service and renders the AI's response (regression — `Full` mode still drives the AI).
- **AC-8** Given a second tool provider (e.g. a `StudentsToolProvider` stub) is registered in DI alongside `CodedValuesToolProvider`, when the engine starts a turn, then the AI sees the union of both providers' tools and the system prompt from whichever `ISystemPromptProvider` is registered.
- **AC-9** Given `SchoolCollab.AI.Chat` is referenced from a new admin landing page (not Settings), when the page renders an `<AiChat Mode="AiChatMode.InputOnly" />`, then the chat input renders with the default title, default intro, and the generic "Ask me anything..." placeholder.
- **AC-10** Given the Aspire AppHost is started, when the user navigates to `/coded-values` in the admin host, then the chat drawer opens correctly and `/api/ai/chat` returns SSE events of the same shape as before the refactor (verified by an existing Playwright test that types a prompt and asserts on the response).

## 11. Edge cases

- **EC-1** A consumer renders `AiChat` with `Enabled="false"`. The component MUST render nothing — no input, no button, no message list, no intro.
- **EC-2** A consumer passes `Title=null`. The component MUST render no heading (matching the current `HideHeader` semantics).
- **EC-3** A consumer passes `Prompt="..."` repeatedly with the same value. The component MUST NOT re-fire the AI (consumed-prompt tracking is preserved from the current `OnParametersSet`).
- **EC-4** A consumer passes `Prompt="..."` while a previous turn is still streaming. The component MUST queue the prompt and run it after the current turn finishes (preserved from the current `_queuedPrompt` mechanism).
- **EC-5** The user navigates away while a turn is streaming. The component MUST cancel the in-flight `HttpClient` request and dispose the JS interop reference (preserved from the current `DisposeAsync`).
- **EC-6** The `AiChatHub` is shared between two `AiChat` instances (inline + drawer). Adding a message to one MUST trigger a re-render of the other, and the messages MUST appear in order (regression — current `HandleHubChanged` pattern is preserved).
- **EC-7** No `IToolProvider` is registered. The engine MUST start, accept a turn, and the AI MUST return a response with no tool calls (the model answers from its own knowledge).
- **EC-8** Two `IToolProvider`s register tools with the same name. The engine MUST log a warning at startup and prefer the first-registered provider at dispatch time.
- **EC-9** The system prompt file is missing or unreadable. The engine MUST log a clear error and refuse to start a turn, surfacing a `ChatUpdate.Error` event with a user-readable message.
- **EC-10** The OpenRouter API key is missing. The engine MUST still start (matching the current fallback behaviour) and the OpenRouter-backed `IChatClient` is registered with a placeholder credential (current behaviour).

## 12. API contracts

### `POST /api/ai/chat`

Request and response shapes are unchanged. SSE events emitted:

```text
event: TextChunk
data: {"text":"..."}

event: ToolCallStart
data: {"callId":"...","friendlyName":"...","argsSummary":"..."}

event: ToolCallEnd
data: {"callId":"...","friendlyName":"...","resultSummary":"...","success":true}

event: Error
data: {"message":"..."}
```

### `GET /api/ai/config`

Response shape unchanged:

```text
{"defaultProvider":"ollama","defaultModel":"gemma3:4b"}
```

### `IToolProvider`

```text
IReadOnlyList<string> ToolNames { get; }
IReadOnlyList<AITool> CreateTools(IReadOnlyList<ChatMessage> history, ILogger logger);
Task<string> DispatchAsync(string toolName, string? args, CancellationToken ct);
```

### `ISystemPromptProvider`

```text
Task<string> GetSystemPromptAsync(CancellationToken ct);
bool IncludesToolList { get; }
```

## 13. Data models

No database schema changes. The migration touches only the source tree, the project references, the sln file, and the AppHost `WithProject<...>()` references.

## 14. Out of scope

- **OS-1** Streaming a second AI provider (e.g. Azure OpenAI) in addition to Ollama / OpenRouter. The current `IChatClientFactory` is preserved as-is. A second provider can land in a follow-up PR.
- **OS-2** A `POST /api/ai/tools` admin endpoint that returns the live tool list. Nice-to-have, not needed for the CodedValues UX. Deferred to a follow-up.
- **OS-3** A second concrete `IToolProvider` (e.g. `StudentsToolProvider`). The plan ships only the CodedValues provider plus a `SchoolCollab.AI.Tools.Stub` example in the spec doc; the second real provider is its own PR.
- **OS-4** Replacing the current `SideDrawer`-based `AiChatPanel` with a more capable drawer. The RCL ships a thin `AiChatPanel` that wraps `SideDrawer` from `Admin.Shared`. A second drawer (e.g. a Radzen-flavoured one) is out of scope.
- **OS-5** A *cross-provider* `toolFilter(string)` callback on `AIChatEngine` that narrows the union of all providers' tools per turn. Out of scope because per-provider narrowing already exists: the CodedValues provider keeps its `SelectToolsForPrompt(...)` intent classifier, so the first PR preserves today's per-turn tool subset exactly. A second provider that wants its own narrowing implements it inside its own `CreateTools(...)`. A global callback is a follow-up if/when a host needs to filter across providers.
- **OS-6** Test migration to a new `SchoolCollab.AI.Chat.Tests.Unit` project. The first PR keeps all chat tests in `SchoolCollab.Settings.Tests.Unit` (where they already live) and only updates the namespaces. A future PR can split them.
- **OS-7** Renaming `CodedValueAIService`-only types that are part of the public SSE payload (e.g. `FriendlyToolNames` map). The current payload is unchanged.
- **OS-8** A visual redesign of the chat. The CSS is moved verbatim; no visual changes.
- **OS-9** Consolidating the two `CodedValuesApiClient` types (and their duplicate DTOs) — `SchoolCollab.Admin.Shared.Services.CodedValuesApiClient` (dropdown client) vs the AI-side client now in `SchoolCollab.AI.Tools.CodedValues`. The first PR leaves both in place and moves only the AI one; merging them into a single shared contracts/SDK project is its own follow-up.

## 15. Phased plan

Each phase ends with `dotnet build` + `dotnet test` green.

### Phase 1 — Create the abstractions library (no behaviour change)

1. Add `src/SchoolCollab.AI.Abstractions/SchoolCollab.AI.Abstractions.csproj` (`Microsoft.NET.Sdk`).
2. Move `ChatUpdate.cs` and `ChatEndpointTypes.cs` (the latter contains `AiProgramMarker`, `ChatRequest`, and `ChatMessageRequest` in one file today) from `SchoolCollab.AI/` to `SchoolCollab.AI.Abstractions/`. Update their namespaces to `SchoolCollab.AI.Abstractions`. (Keep `ChatEndpointTypes.cs` as a single file; do not split it unless a later step asks for it.)
3. Add `IToolProvider.cs` and `ISystemPromptProvider.cs` (interface only, no implementation).
4. Update `SchoolCollab.AI/SchoolCollab.AI.csproj` to reference the new abstractions project; remove the now-moved files.
5. Update `SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj` to reference the new abstractions project (it currently references the old `SchoolCollab.AI` project just for the moved types).
6. `dotnet build` + `dotnet test` green.

Exit gate: `SchoolCollab.AI.Abstractions` exists, all `ChatUpdate` / `ChatRequest` references resolve through it, the baseline unit-test count (captured at refactor start — see NFR-2) passes.

### Phase 2 — Rename `SchoolCollab.AI` → `SchoolCollab.AI.Server`

1. Rename the project directory `src/SchoolCollab.AI/` → `src/SchoolCollab.AI.Server/`.
2. Rename `SchoolCollab.AI.csproj` → `SchoolCollab.AI.Server.csproj`. Update the `<RootNamespace>` and `<AssemblyName>` to `SchoolCollab.AI.Server`.
3. Update the `AppHost`'s `AddProject<Projects.SchoolCollab_AI>(...)` to `AddProject<Projects.SchoolCollab_AI_Server>(...)` (and the resource name stays `settings-ai` to keep Aspire's published-name stable).
4. Update `SchoolCollab.sln` to rename the project and the GUID is preserved.
5. `dotnet build` + `dotnet test` green.

Exit gate: Aspire dashboard still shows `settings-ai` resource, the baseline unit-test count (NFR-2) passes, no source-level renames happened — only the project + assembly names changed.

### Phase 3 — Create the RCL + extract the chat surface

1. Add `src/SchoolCollab.AI.Chat/SchoolCollab.AI.Chat.csproj` (`Microsoft.NET.Sdk.Razor`).
2. Move:
   - `src/SchoolCollab.Admin.Shared/Services/AiChatClient.cs` → `src/SchoolCollab.AI.Chat/Services/AiChatClient.cs` (namespace → `SchoolCollab.AI.Chat.Services`).
   - `src/Settings/SchoolCollab.Settings.Admin/Services/CodedValuesChatHub.cs` → `src/SchoolCollab.AI.Chat/Services/AiChatHub.cs` (namespace → `SchoolCollab.AI.Chat.Services`, type rename).
   - `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/CodedValues/CodedValuesChat.razor` (+ .css) → `src/SchoolCollab.AI.Chat/Components/AiChat.razor` (+ .css). Rename `CodedValuesChatMode` → `AiChatMode`, `CodedValuesChat.ChatMessageItem` → `AiChatMessage`, `CodedValuesChat.ChatStreamingState` → `AiChatStreamingState`. Update the JS import path to `./_content/SchoolCollab.AI.Chat/js/chatInput.js`.
   - `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/CodedValues/CodedValuesChatPanel.razor` (+ .css) → `src/SchoolCollab.AI.Chat/Components/AiChatPanel.razor` (+ .css). Update inner chat reference to `AiChat`.
   - `src/Settings/SchoolCollab.Settings.Admin/wwwroot/js/chatInput.js` → `src/SchoolCollab.AI.Chat/wwwroot/js/chatInput.js`.
3. Remove the hard-coded `Nav.NavigateTo("/coded-values/...")` from `AiChat.razor`. Replace with a new `EventCallback<ToolCallDisplay> OnToolCallCompleted` parameter.
4. Add `src/SchoolCollab.AI.Chat/Components/_Imports.razor` with the RCL's required `@using` directives.
5. Add `src/SchoolCollab.AI.Chat/Services/AiChatModule.cs` with `AddAiChat(...)`.
6. Update `SchoolCollab.Admin.Shared` to drop its `ProjectReference` to the old `SchoolCollab.AI` project (replace with `SchoolCollab.AI.Abstractions`).
7. Update `SchoolCollab.Settings.Admin` to add a `ProjectReference` to `SchoolCollab.AI.Chat`; delete the moved files.
8. Update the CodedValues landing page (`Index.razor`):
   - Replace `<CodedValuesChat .../>` with `<AiChat .../>` and the new namespace.
   - Replace the inline `OnPromptSubmitted` handler with the new parameter name on `AiChat`.
   - Add a new handler for `OnToolCallCompleted` that does the `Nav.NavigateTo("/coded-values/...")` that the chat used to do internally.
9. `dotnet build` + `dotnet test` green. All bUnit + Playwright tests for the CodedValues chat pass with namespace updates.

Exit gate: chat still works end-to-end on the CodedValues landing page, but the component, panel, hub, JS, and CSS now live in the RCL.

### Phase 4 — Genericise the server

1. Move the CodedValues-specific code out of `SchoolCollab.AI.Server`:
   - `Services/CodedValueAIService.cs` → `src/SchoolCollab.AI.Tools.CodedValues/CodedValuesToolProvider.cs` (split into `CodedValuesToolProvider : IToolProvider` + a `CodedValuesSystemPromptProvider : ISystemPromptProvider`).
   - `Services/CodedValuesApiClient.cs` + `ICodedValuesApiClient.cs` + `BulkChildItem.cs` → `src/SchoolCollab.AI.Tools.CodedValues/` (unchanged shape, namespace updated).
   - `Prompts/ai-system-prompt.md` → `src/SchoolCollab.AI.Tools.CodedValues/Prompts/coded-values-system-prompt.md`.
2. Create `src/SchoolCollab.AI.Server/Services/AIChatEngine.cs` — the new generic engine. It takes `IEnumerable<IToolProvider>`, `ISystemPromptProvider`, `IChatClientFactory`, and `ILogger<AIChatEngine>` via DI. Its `ChatAsync` method:
   - Awaits `ISystemPromptProvider.GetSystemPromptAsync(...)`.
   - Iterates `IToolProvider`s; for each, calls `CreateTools(history, logger)` (which lets the provider apply its own per-turn narrowing — the CodedValues provider runs its `SelectToolsForPrompt(...)` classifier here) and concatenates the returned `AITool`s.
   - Runs the existing `IChatClientFactory.CreateClient()` flow against `IChatClient`, routing `FunctionCallContent` / `FunctionResultContent` to the matching `IToolProvider.DispatchAsync(...)`.
3. Update `SchoolCollab.AI.Server/Program.cs`:
   - Register `IToolProvider`s and `ISystemPromptProvider`s via `IServiceCollection`.
   - Replace `AddSingleton<CodedValueAIService>` with `AddSingleton<AIChatEngine>`.
   - The endpoint map stays the same shape but resolves `AIChatEngine` instead of `CodedValueAIService`.
4. Update `SchoolCollab.AI.Tools.CodedValues/SchoolCollab.AI.Tools.CodedValues.csproj` to be referenced by `SchoolCollab.AI.Server`'s `Program.cs` (or registered via an extension method `AddCodedValuesAiTools()` that the AppHost's `Program.cs` calls).
5. `dotnet build` + `dotnet test` green. End-to-end CodedValues chat still works identically.

Exit gate: a second `IToolProvider` (e.g. a 1-tool stub) can be added in a future PR without touching `SchoolCollab.AI.Server`.

### Phase 5 — Tests + docs + cleanup

1. Add `tests/SchoolCollab.AI.Chat.Tests.Unit/SchoolCollab.AI.Chat.Tests.Unit.csproj` with the bUnit chat tests **copied** (not moved) from `SchoolCollab.Settings.Tests.Unit`, with namespaces updated to the RCL/server. The `SchoolCollab.Settings.Tests.Unit` project keeps its originals so its passing count does **not** drop (NFR-2 is preserved); the new project runs the same assertions against the new namespaces. `CodedValueAIServiceToolSelectionTests.cs` is the one test that is **rewritten** (FR-14) to target `CodedValuesToolProvider.CreateTools(...)`; both the Settings copy and the new-project copy are updated to the new target. Deletion of the Settings-project originals is deferred to a later cleanup PR.
   - Out of scope per OS-6: this is the *second* PR; the first PR keeps all tests in `SchoolCollab.Settings.Tests.Unit` and only updates namespaces (and rewrites the tool-selection test).
2. Add `documents/solution/ai-chat-rcl.md` describing the new architecture, the `IToolProvider` / `ISystemPromptProvider` contracts, and a worked example of adding a second provider.
3. Delete the legacy `src/SchoolCollab.AI/` directory.
4. `dotnet build` + `dotnet test` green.
5. Update `documents/configuration.md` to reflect the renamed server resource.

## 16. Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| CodedValues chat behaviour drifts during the rename | High | High | Phase 1 + Phase 2 + Phase 3 are pure renames; no behaviour change. Phase 4's "byte-for-byte" goal (including the per-turn `SelectToolsForPrompt` tool subset — FR-10) is enforced by the baseline unit tests + the Playwright smoke test. |
| `CodedValueAIService` has hidden coupling to the HTTP context that breaks when lifted into a tool provider | Medium | Medium | The provider's `CreateTools` takes the chat `history` + a logger — **no** `IHttpContextAccessor` (the current singleton never reads per-request context at tool-creation time). All existing tool dispatches are HTTP-agnostic; the `ICodedValuesApiClient` is injected, not constructed from the context. If a future provider needs per-request claims, it resolves a scoped service inside `DispatchAsync`. |
| JS asset path change breaks the keyboard handler | Medium | High | The asset path change is one of the few real risks; covered by the existing Playwright test that types into the input and asserts the prompt is sent. |
| Aspire AppHost resource rename breaks local dev | Low | High | Resource name stays `settings-ai`; only the project path + the `AddProject<...>` generic argument change. Verified by `dotnet run --project src/AppHost` at the end of Phase 2. |
| Duplicate `CodedValuesApiClient` types (Admin.Shared vs AI) get conflated | Medium | Medium | Two distinct types exist today. The refactor moves only the AI one into `SchoolCollab.AI.Tools.CodedValues`; `Admin.Shared` keeps its own for dropdowns and gains no reference to the tools project (see §3). Consolidation is deferred to OS-9. |
| Two `IToolProvider`s register the same tool name | Low | Low | EC-8: engine logs a warning + uses first-registered. Documented behaviour. |

## 17. Acceptance walk-through (post-implementation)

To verify the plan once implemented, run:

```text
# 1. Build the whole solution
dotnet build SchoolCollab.sln

# 2. Run the unit tests
dotnet test tests/SchoolCollab.Settings.Tests.Unit --nologo

# 3. Start the Aspire AppHost
dotnet run --project src/AppHost

# 4. In another shell, run the Playwright smoke test for the CodedValues chat
dotnet test tests/SchoolCollab.Settings.Tests.Playwright --filter "CodedValuesChatPanel"
```

Expected: the baseline unit-test count (NFR-2) passes, the Playwright smoke test types a prompt and verifies the response, the AppHost dashboard shows `settings-ai` as a healthy resource, and the CodedValues landing page renders + behaves identically to the pre-refactor state.

## 18. Open questions for the user

1. **Provider lifetime** — `IToolProvider` / `ISystemPromptProvider` are registered as **singletons** (the current `CodedValueAIService` is `AddSingleton` and holds no per-request state — only cached prompt + tool list). `CreateTools` receives the per-turn `history` and a logger as arguments, so no per-request state is stored on the provider. **Decided** (matches existing registration); left here for confirmation.
2. **System prompt source** — should the CodedValues provider load its prompt from embedded resources (current behaviour) or from a configurable path under `appsettings.json`? The current `ai-system-prompt.md` is embedded, so the migration is a no-op if we keep that.
3. **Drawer component** — should the RCL ship its own `AiChatPanel` that wraps `SideDrawer` from `Admin.Shared`, or should `AiChatPanel` be a thin adapter in the consumer project? Plan assumes the former (RCL ships the panel) so a consumer can drop in `<AiChatPanel ... />` in one line.
4. **Chat title / intro in the drawer** — should the drawer's title come from `AiChatPanel.Title` (default `"✨ AI Assistant"`) or from `AiChat.Title`? The current behaviour has the drawer passing `HideHeader="true"` to the inner chat and supplying its own title via the `SideDrawer` header. The plan keeps that: `AiChatPanel.Title` is the drawer's title, `AiChat.Title` is the inline heading on a `Full`-mode chat.
5. **Backward compatibility for `SchoolCollab.AI` assembly** — anyone with a downstream reference will break. Acceptable since this is a pre-1.0 codebase; the migration commit is a hard rename. Confirm.
