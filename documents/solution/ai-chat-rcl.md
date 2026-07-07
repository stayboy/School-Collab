# Reusable AI Chat (RCL) — Architecture

This document describes the reusable AI chat surface extracted by the
`ai-chat-rcl-spec` refactor and shows how to plug a **second** bounded context's
tools into the same chat UI/engine without touching the chat code itself.

## 1. The four projects

| Project | SDK | Role |
|---------|-----|------|
| `SchoolCollab.AI.Abstractions` | class lib | The shared contract: `ChatUpdate`, `ChatRequest`, `ChatMessageRequest`, `AiProgramMarker`, plus the two plug-in interfaces `IToolProvider` and `ISystemPromptProvider`. Referenced by both the client (RCL) and the server, so neither has to reference the other. |
| `SchoolCollab.AI.Chat` | Razor RCL | The domain-agnostic chat UI: `AiChat` + `AiChatPanel` components, `AiChatHub` (scoped mirror bridge), `AiChatClient` (HttpClient to the AI server), `AiChatModule.AddAiChat(...)` DI helper, the `chatInput.js` keyboard handler, and the scoped CSS. Depends only on `Abstractions` + `Admin.Shared` — never on the server or any tool provider. |
| `SchoolCollab.AI.Server` | Web | The generic chat host: `AIChatEngine` (the multi-round streaming + tool-call loop), `IChatClientFactory`/`ChatClientFactory`, `ChatModelResolver`, `AiTextCleaner`, and the `/api/ai/chat` + `/api/ai/config` endpoints. Loads tools from every registered `IToolProvider` and the system prompt from `ISystemPromptProvider`. |
| `SchoolCollab.AI.Tools.CodedValues` | class lib | The first concrete provider: `CodedValuesToolProvider` (9 tools + per-turn `SelectToolsForPrompt` narrowing + SSE formatting) and `CodedValuesSystemPromptProvider` (the Coded Values system prompt). Wired in by `AddCodedValuesAiTools(...)`. |

A second bounded context (Assignments, Students, …) ships its own
`SchoolCollab.AI.Tools.Xxx` project alongside `SchoolCollab.AI.Tools.CodedValues`
and registers it with a parallel `AddXxxAiTools()` call — the engine picks it up
automatically. The chat UI, drawer, SSE protocol, JS handler, mirror hub, and
HTTP transport are reused unchanged.

## 2. The plug-in contracts (`SchoolCollab.AI.Abstractions`)

```csharp
public interface IToolProvider
{
    IReadOnlyList<string> ToolNames { get; }

    // Build the AITool list for the current turn. The provider MAY narrow the
    // list per prompt (CodedValues runs its SelectToolsForPrompt intent
    // classifier here); the engine concatenates every provider's result.
    IReadOnlyList<AITool> CreateTools(IReadOnlyList<ChatMessage> history, ILogger logger);

    // Route a tool call to the local implementation.
    Task<string> DispatchAsync(string toolName, string? args, CancellationToken ct);

    // SSE payload formatting (emitted in ToolCallStart / ToolCallEnd events):
    string GetFriendlyName(string toolName);      // "create_bulk_values" -> "Create Bulk Values"
    string FormatArgsSummary(string toolName, string? args);  // "parent: CNTRY"
    string FormatResultSummary(string toolName, string result); // "3 values created"
}

public interface ISystemPromptProvider
{
    Task<string> GetSystemPromptAsync(CancellationToken ct);
    bool IncludesToolList { get; }   // true if the engine should append the live tool list to the framing
}
```

The engine builds a `toolName -> IToolProvider` map at construction. On a name
collision it logs a warning and the **first-registered** provider wins at
dispatch time (EC-8).

## 3. The RCL public surface (`SchoolCollab.AI.Chat`)

`AiChat` (`SchoolCollab.AI.Chat.Components.AiChat`) — the inline chat. Key
parameters: `Mode` (`Full`/`DisplayOnly`/`InputOnly`), `HideHeader`, `ShowIntro`,
`SuppressResponseDisplay`, `Title` (default `"✨ AI Assistant"`), `Intro`,
`Placeholder` (default `"Ask me anything..."`), `Enabled` (renders nothing when
`false`), `Prompt` (forwarded prompt), `ExternalMessages`/
`ExternalStreamingState` (mirror source), and the callbacks `OnMessageAdded`,
`OnCleared`, `OnStreamingStateChanged`, `OnPromptSending`, `OnPromptSent`,
`OnPromptSubmitted`, and `OnToolCallCompleted`.

The chat is domain-agnostic: it never navigates on a tool call. Successful tool
completions are raised as `EventCallback<ToolCallDisplay> OnToolCallCompleted`;
the host decides what to do (the CodedValues landing page's handler recognises
`"Create Bulk Values"` and navigates to the children page).

`AiChatPanel` (`SchoolCollab.AI.Chat.Components.AiChatPanel`) — wraps
`SideDrawer` (from `Admin.Shared`) and hosts a `Full`-mode `AiChat` mirrored
through the shared `AiChatHub`. Parameters: `Open`/`OpenChanged`, `Prompt`,
`Title`/`Width`/`CancelText`, `Enabled`, and `OnToolCallCompleted` (bubbled up
from the inner chat).

`AiChatModule.AddAiChat(this IServiceCollection, Action<HttpClient> configure)`
wires `AiChatClient` (HttpClient, the host supplies its AI server's base
address) + the scoped `AiChatHub`. Call once from the host's service pipeline:

```csharp
builder.Services.AddAiChat(c => c.BaseAddress = new Uri("https+http://settings-ai"));
```

The JS keyboard handler ships as a static web asset at
`_content/SchoolCollab.AI.Chat/js/chatInput.js` (no `<script>` tag needed); the
scoped CSS is bundled automatically.

## 4. The server (`SchoolCollab.AI.Server`)

`AIChatEngine.ChatAsync` is the single streaming + tool-call loop. Per turn it:

1. awaits `ISystemPromptProvider.GetSystemPromptAsync` and prepends it to the
   history;
2. concatenates `CreateTools(history, logger)` from every registered
   `IToolProvider` (each provider narrows its own bag);
3. streams `GetStreamingResponseAsync`, accumulating `FunctionCallContent`,
   yielding `ChatUpdate.ToolCallStart` with the owning provider's
   `GetFriendlyName`/`FormatArgsSummary`;
4. dispatches each call to the owning provider's `DispatchAsync`, yielding
   `ChatUpdate.ToolCallEnd` with `FormatResultSummary`;
5. on the final text-only round, yields `ChatUpdate.TextChunk`.

Model resolution, text cleaning (`AiTextCleaner`), and the HTTP-status →
`ChatUpdate.Error` mapping are unchanged from the former `CodedValueAIService`.
The SSE protocol emitted by `POST /api/ai/chat` is **unchanged** (NFR-3).

## 5. Worked example — adding a second tool provider (stub `Students`)

1. **Create the project** `src/SchoolCollab.AI.Tools.Students/` (class lib),
   referencing `SchoolCollab.AI.Abstractions`:

   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <RootNamespace>SchoolCollab.AI.Tools.Students</RootNamespace>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="Microsoft.Extensions.AI" />
       <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
     </ItemGroup>
     <ItemGroup>
       <ProjectReference Include="..\SchoolCollab.AI.Abstractions\SchoolCollab.AI.Abstractions.csproj" />
     </ItemGroup>
   </Project>
   ```

2. **Implement the provider** (one stub tool):

   ```csharp
   using Microsoft.Extensions.AI;
   using Microsoft.Extensions.Logging;
   using SchoolCollab.AI.Abstractions;

   namespace SchoolCollab.AI.Tools.Students;

   public sealed class StudentsToolProvider : IToolProvider
   {
       public IReadOnlyList<string> ToolNames { get; } = ["students.search"];

       public IReadOnlyList<AITool> CreateTools(IReadOnlyList<ChatMessage> history, ILogger logger)
       {
           var tool = AIFunctionFactory.Create(SearchAsync, "students.search",
               "Search students by name fragment. Returns a compact list of matches.");
           return [tool];
       }

       public Task<string> DispatchAsync(string toolName, string? args, CancellationToken ct)
           => toolName == "students.search" ? SearchAsync(args ?? "", ct) : Task.FromResult($"Unknown tool: {toolName}");

       public string GetFriendlyName(string toolName) => toolName == "students.search" ? "Search Students" : toolName;
       public string FormatArgsSummary(string toolName, string? args) => string.IsNullOrEmpty(args) ? "" : $"q: {args}";
       public string FormatResultSummary(string toolName, string result) => result.Length <= 150 ? result : result[..150] + "…";

       private static Task<string> SearchAsync(string query, CancellationToken ct)
           => Task.FromResult($"(stub) no student search backend yet for '{query}'");
   }
   ```

   (System prompt: either reuse the engine's default or add a
   `StudentsSystemPromptProvider : ISystemPromptProvider`.)

3. **Register it** from the AI server's startup, next to the CodedValues one:

   ```csharp
   builder.Services.AddCodedValuesAiTools(c => c.BaseAddress = new Uri("https+http://settings-api"));
   builder.Services.AddStudentsAiTools();   // registers StudentsToolProvider (+ ISystemPromptProvider)
   builder.Services.AddSingleton<AIChatEngine>();
   ```

   (If the stub needs no HttpClient, the `AddStudentsAiTools()` extension just
   does `services.AddSingleton<IToolProvider, StudentsToolProvider>()`.)

4. **Host it** from a landing page — drop in the RCL in one line:

   ```razor
   @using SchoolCollab.AI.Chat.Components
   <AiChat Mode="AiChatMode.InputOnly"
           Title="🎓 Student Assistant"
           Intro="Ask me to find a student."
           Placeholder="Search students…"
           OnPromptSubmitted="@OnStudentPromptAsync" />
   ```

   The model now sees the **union** of the CodedValues + Students tools; a tool
   call to `students.search` is routed to `StudentsToolProvider.DispatchAsync`
   and surfaced in the SSE stream with `"Search Students"` as its friendly name.
   No change to the chat UI, drawer, JS, hub, or the `/api/ai/chat` endpoint.

## 6. Out of scope (follow-up PRs)

- Splitting the chat bUnit tests into a dedicated `SchoolCollab.AI.Chat.Tests.Unit`
  project (the first PR keeps them in `SchoolCollab.Settings.Tests.Unit` — OS-6).
- A `POST /api/ai/tools` admin endpoint returning the live tool list (OS-2).
- Consolidating the two `CodedValuesApiClient` types (Admin.Shared dropdown client
  vs the AI tools client) into a single shared contracts/SDK project (OS-9).
- A cross-provider `toolFilter(string)` callback on `AIChatEngine` (OS-5) — each
  provider already narrows its own tools per turn.