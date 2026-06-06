using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace SchoolCollab.CodedValues.Admin.Services;

/// <summary>
/// AI-powered service for populating coded values using natural language prompts.
/// Uses Microsoft.Extensions.AI with function calling to let the AI model create
/// and manage coded values through the existing API.
/// </summary>
public sealed class CodedValueAIService
{
    private readonly IChatClient _chatClient;
    private readonly CodedValuesApiClient _api;
    private readonly ILogger<CodedValueAIService> _logger;
    private readonly IHostEnvironment _hostEnv;

    private readonly List<AITool> _tools;

    // System prompt loaded dynamically from wwwroot/ai-system-prompt.md
    private string? _cachedSystemPrompt;
    private DateTime _systemPromptLastWrite;

    private static readonly Dictionary<string, string> FriendlyToolNames = new()
    {
        ["list_coded_value_categories"] = "List Categories",
        ["get_coded_value_by_code"] = "Get By Code",
        ["create_coded_value"] = "Create Value",
        ["create_bulk_values"] = "Create Bulk Values",
        ["set_attribute_definition"] = "Define Attribute",
        ["set_attribute"] = "Set Attribute"
    };

    public CodedValueAIService(IChatClient chatClient, CodedValuesApiClient api, ILogger<CodedValueAIService> logger, IHostEnvironment hostEnv)
    {
        _chatClient = chatClient;
        _api = api;
        _logger = logger;
        _hostEnv = hostEnv;

        _tools =
        [
            AIFunctionFactory.Create(ListCategoriesAsync, "list_coded_value_categories", "Lists all root-level coded value categories. Returns an array of category objects with id, code, name, description, and children count."),
            AIFunctionFactory.Create(GetByCodeAsync, "get_coded_value_by_code", "Gets a coded value by its unique code. Returns the full coded value including its children, attribute definitions (on parents), and attributes (on children)."),
            AIFunctionFactory.Create(CreateCodedValueAsync, "create_coded_value", "Creates a new coded value. Accepts code (unique identifier like CNTRY), name (display name like Countries), optional description, optional parentId for creating a child value, and optional displayOrder."),
            AIFunctionFactory.Create(CreateBulkValuesAsync, "create_bulk_values", "Creates multiple child values under a parent coded value in one call. Accepts parentCode (the code of the parent category) and an array of child items, each with code and name. Optionally include description. Use this to populate a category with many values at once, e.g., countries under a Countries category."),
            AIFunctionFactory.Create(SetAttributeDefinitionAsync, "set_attribute_definition", "Defines an attribute on a PARENT coded value. Attribute definitions describe what metadata children should have. Must be called on the parent before setting attribute values on children. Accepts: parentCode (the parent's code, e.g. AI-MODELS), key (attribute key like 'weight'), displayName (human-readable label), dataType (0=Text,1=Integer,2=Decimal,3=Boolean,4=Date,5=DateTime,6=Time,7=CodedValue), sourceCode (required only if dataType=7, references another parent code for dropdown values), isRequired, allowMultiple."),
            AIFunctionFactory.Create(SetAttributeAsync, "set_attribute", "Sets an attribute value on a coded value. The attribute definition with the same key must already exist on the PARENT of this coded value. Accepts: code (the coded value's code), key (attribute key that matches a definition on the parent), value (the value as a string).")
        ];
    }

    /// <summary>
    /// Loads the system prompt from wwwroot/ai-system-prompt.md, with caching and auto-reload on file change.
    /// </summary>
    private string GetSystemPrompt()
    {
        var promptFile = Path.Combine(_hostEnv.ContentRootPath, "wwwroot", "ai-system-prompt.md");

        try
        {
            var lastWrite = System.IO.File.GetLastWriteTimeUtc(promptFile);
            if (_cachedSystemPrompt is not null && lastWrite == _systemPromptLastWrite)
                return _cachedSystemPrompt;

            _cachedSystemPrompt = System.IO.File.ReadAllText(promptFile);
            _systemPromptLastWrite = lastWrite;
            _logger.LogInformation("Loaded system prompt from {Path} ({Length} chars)", promptFile, _cachedSystemPrompt.Length);
            return _cachedSystemPrompt;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read system prompt from {Path}, using cached version", promptFile);
            return _cachedSystemPrompt ?? FallbackSystemPrompt;
        }
    }

    private const string FallbackSystemPrompt = """
        You are a helpful assistant for managing coded values in a school collaboration system.
        Coded values are hierarchical lookup tables. Each has a unique code, a name, and an optional description.
        Parents define categories; children are the actual values.

        ## Critical rules for responses
        1. Never list or describe tool/function calls in your text response. Just use the tools silently and present results.
        2. Never output raw JSON or technical data structures. Always use human-readable format.
        3. Always present coded-value data as a Markdown table before creating anything.
        4. After a tool succeeds, describe the outcome in plain English.

        ## Workflow
        1. Identify the parent coded value from the user's request. If ambiguous, ask. If not found, create it.
        2. Present proposed values as a Markdown table. Ask: "Shall I create these coded values?"
        3. When user confirms, create values using the bulk creation tool.
        4. Confirm creation in plain English.
        """;

    /// <summary>
    /// Sends conversation history to the AI and yields structured updates (text chunks,
    /// tool-call progress, errors). Handles multi-turn tool-call loops.
    /// Text from tool-call rounds is collected for message history but NOT streamed to UI —
    /// only the final round's text is yielded, preventing function-call JSON leakage.
    /// </summary>
    public async IAsyncEnumerable<ChatUpdate> ChatAsync(
        IReadOnlyList<ChatMessage> history,
        string? model = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Processing AI chat with {Count} history messages", history.Count);

        var systemPrompt = GetSystemPrompt();
        var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
        messages.AddRange(history);

        var options = model is not null
            ? new ChatOptions { Tools = _tools, ModelId = model }
            : new ChatOptions { Tools = _tools };

        var totalToolCalls = 0;
        const int maxToolCallRounds = 10;

        while (totalToolCalls < maxToolCallRounds)
        {
            var roundText = new StringBuilder();
            var toolCallsByCallId = new Dictionary<string, (string Name, string? Args)>();

            await foreach (var chunk in _chatClient.GetStreamingResponseAsync(messages, options, ct).WithCancellation(ct))
            {
                // Collect function call content from streaming updates
                if (chunk.Contents is not null)
                {
                    foreach (var content in chunk.Contents)
                    {
                        if (content is FunctionCallContent fc && fc.Name is not null)
                        {
                            var callId = fc.CallId ?? Guid.NewGuid().ToString();
                            if (!toolCallsByCallId.ContainsKey(callId))
                            {
                                var args = fc.Arguments is not null
                                    ? JsonSerializer.Serialize(fc.Arguments)
                                    : null;
                                toolCallsByCallId[callId] = (fc.Name, args);

                                var friendlyName = GetFriendlyToolName(fc.Name);
                                var argsSummary = FormatArgsSummary(fc.Name, args);
                                yield return new ChatUpdate.ToolCallStart(callId, friendlyName, argsSummary);
                            }
                        }
                    }
                }

                // Collect text for message history (always), but do NOT stream to UI during tool-call rounds
                if (chunk.Text is not null)
                    roundText.Append(chunk.Text);
            }

            // Build assistant message with text + function call content items
            var assistantContents = new List<AIContent>();
            if (roundText.Length > 0)
                assistantContents.Add(new TextContent(roundText.ToString()));
            foreach (var (callId, (name, args)) in toolCallsByCallId)
            {
                var arguments = ParseArgumentsDictionary(args);
                assistantContents.Add(new FunctionCallContent(callId, name, arguments));
            }
            if (assistantContents.Count > 0)
                messages.Add(new ChatMessage(ChatRole.Assistant, assistantContents));

            if (toolCallsByCallId.Count == 0)
            {
                // Final round — no more tool calls. Stream the clean text to UI.
                var finalText = CleanModelText(roundText.ToString());
                if (!string.IsNullOrEmpty(finalText))
                    yield return new ChatUpdate.TextChunk(finalText);

                break;
            }

            totalToolCalls += toolCallsByCallId.Count;

            // Dispatch each tool call and add results
            foreach (var (callId, (name, args)) in toolCallsByCallId)
            {
                var result = await DispatchToolCallAsync(name, args, ct);
                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, result)]));

                var friendlyName = GetFriendlyToolName(name);
                var resultSummary = FormatResultSummary(name, result);
                var success = !result.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
                yield return new ChatUpdate.ToolCallEnd(callId, friendlyName, resultSummary, success);
            }
        }

        if (totalToolCalls >= maxToolCallRounds)
        {
            _logger.LogWarning("Reached max tool-call rounds ({Max}), stopping", maxToolCallRounds);
            yield return new ChatUpdate.Error($"Reached maximum tool-call limit ({maxToolCallRounds}). Please continue your request.");
        }

        _logger.LogInformation("AI chat completed with {ToolCalls} tool calls", totalToolCalls);
    }

    private static IDictionary<string, object?> ParseArgumentsDictionary(string? args)
    {
        var arguments = new Dictionary<string, object?>();
        if (string.IsNullOrEmpty(args)) return arguments;
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(args);
            if (parsed is not null)
            {
                foreach (var kvp in parsed)
                {
                    arguments[kvp.Key] = kvp.Value.ValueKind switch
                    {
                        JsonValueKind.String => kvp.Value.GetString(),
                        JsonValueKind.Number => kvp.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => kvp.Value.GetRawText()
                    };
                }
            }
        }
        catch { /* ignore parse errors, partial args ok */ }
        return arguments;
    }
    private async Task<string> DispatchToolCallAsync(string toolName, string? arguments, CancellationToken ct)
    {
        _logger.LogDebug("Dispatching tool call: {ToolName}", toolName);
        try
        {
            var result = toolName switch
            {
                "list_coded_value_categories" => await ListCategoriesAsync(ct),
                "get_coded_value_by_code" => await DispatchGetByCodeAsync(arguments, ct),
                "create_coded_value" => await DispatchCreateCodedValueAsync(arguments, ct),
                "create_bulk_values" => await DispatchCreateBulkValuesAsync(arguments, ct),
                "set_attribute_definition" => await DispatchSetAttributeDefinitionAsync(arguments, ct),
                "set_attribute" => await DispatchSetAttributeAsync(arguments, ct),
                _ => $"Unknown tool: {toolName}"
            };
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool call {ToolName} failed", toolName);
            return $"Error: {ex.Message}";
        }
    }

    // --- Dispatch helpers that parse JSON arguments ---

    private async Task<string> DispatchGetByCodeAsync(string? args, CancellationToken ct)
    {
        var code = ExtractStringArg(args, "code");
        return code is null ? "Error: missing 'code' argument" : await GetByCodeAsync(code, ct);
    }

    private async Task<string> DispatchCreateCodedValueAsync(string? args, CancellationToken ct)
    {
        var code = ExtractStringArg(args, "code") ?? "";
        var name = ExtractStringArg(args, "name") ?? "";
        var description = ExtractStringArg(args, "description");
        return await CreateCodedValueAsync(code, name, description, ct);
    }

    private async Task<string> DispatchCreateBulkValuesAsync(string? args, CancellationToken ct)
    {
        var parentCode = ExtractStringArg(args, "parentCode") ?? "";
        var children = ExtractChildrenArg(args);
        return await CreateBulkValuesAsync(parentCode, children, ct);
    }

    private async Task<string> DispatchSetAttributeDefinitionAsync(string? args, CancellationToken ct)
    {
        var parentCode = ExtractStringArg(args, "parentCode") ?? "";
        var key = ExtractStringArg(args, "key") ?? "";
        var displayName = ExtractStringArg(args, "displayName");
        var dataType = ExtractIntArg(args, "dataType") ?? 0;
        var sourceCode = ExtractStringArg(args, "sourceCode");
        var isRequired = ExtractBoolArg(args, "isRequired") ?? false;
        var allowMultiple = ExtractBoolArg(args, "allowMultiple") ?? false;
        return await SetAttributeDefinitionAsync(parentCode, key, displayName, dataType, sourceCode, isRequired, allowMultiple, ct);
    }

    private async Task<string> DispatchSetAttributeAsync(string? args, CancellationToken ct)
    {
        var code = ExtractStringArg(args, "code") ?? "";
        var key = ExtractStringArg(args, "key") ?? "";
        var value = ExtractStringArg(args, "value") ?? "";
        return await SetAttributeAsync(code, key, value, ct);
    }

    private static string? ExtractStringArg(string? args, string name)
    {
        if (string.IsNullOrEmpty(args)) return null;
        try
        {
            using var doc = JsonDocument.Parse(args);
            if (doc.RootElement.TryGetProperty(name, out var el))
                return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
            return null;
        }
        catch { return null; }
    }

    private static int? ExtractIntArg(string? args, string name)
    {
        if (string.IsNullOrEmpty(args)) return null;
        try
        {
            using var doc = JsonDocument.Parse(args);
            if (doc.RootElement.TryGetProperty(name, out var el))
                return el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;
            return null;
        }
        catch { return null; }
    }

    private static bool? ExtractBoolArg(string? args, string name)
    {
        if (string.IsNullOrEmpty(args)) return null;
        try
        {
            using var doc = JsonDocument.Parse(args);
            if (doc.RootElement.TryGetProperty(name, out var el))
                return el.ValueKind == JsonValueKind.True ? true : el.ValueKind == JsonValueKind.False ? false : null;
            return null;
        }
        catch { return null; }
    }

    private static BulkChildItem[] ExtractChildrenArg(string? args)
    {
        if (string.IsNullOrEmpty(args)) return [];
        try
        {
            using var doc = JsonDocument.Parse(args);
            if (!doc.RootElement.TryGetProperty("children", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];
            return arr.EnumerateArray().Select(el => new BulkChildItem(
                el.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
                el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                el.TryGetProperty("description", out var d) ? d.GetString() : null
            )).ToArray();
        }
        catch { return []; }
    }
    // --- Text cleaning ---

    private static string CleanModelText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove model-internal thinking/scratchpad tags
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<thinking>[\s\S]*?</thinking>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<scratchpad>[\s\S]*?</scratchpad>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<reflection>[\s\S]*?</reflection>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove function/tool definition JSON that local LLMs leak as text.
        // Pattern 1: {"type": "function", ...} blocks (with balanced brace matching)
        text = RemoveJsonBlocksContaining(text, "\"type\"\\s*:\\s*\"function\"");
        // Pattern 2: {'type': 'function', ...} blocks
        text = RemoveJsonBlocksContaining(text, "'type'\\s*:\\s*'function'");
        // Pattern 3: {"function": {"name": "...", ...}} blocks
        text = RemoveJsonBlocksContaining(text, "\"function\"\\s*:\\s*\\{");

        // Remove tool invocation lines: function_name(arg1="value", ...)
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"^\s*(list_coded_value_categories|get_coded_value_by_code|create_coded_value|create_bulk_values|set_attribute_definition|set_attribute)\s*\(.*?\)\s*[;,]?\s*$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);

        // Remove lines that are just tool names with arrows/prefixes
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"^\s*(?:→|->|≫|>>|▸|•)\s*(list_coded_value_categories|get_coded_value_by_code|create_coded_value|create_bulk_values|set_attribute_definition|set_attribute)\s*$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);

        // Remove standalone known tool-name lines
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"^\s*(list_coded_value_categories|get_coded_value_by_code|create_coded_value|create_bulk_values|set_attribute_definition|set_attribute)\s*$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);

        // Remove lines that are just 'name': 'tool_name' or "name": "tool_name"
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"^\s*['""]name['""]\s*:\s*['""](list_coded_value_categories|get_coded_value_by_code|create_coded_value|create_bulk_values|set_attribute_definition|set_attribute)['""].*$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);

        // Collapse excessive blank lines
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\r?\n){3,}", "\n\n");

        return text.Trim();
    }

    /// <summary>
    /// Removes JSON object blocks (balanced braces) that contain a specific pattern.
    /// Handles nested braces correctly for multi-line JSON.
    /// </summary>
    private static string RemoveJsonBlocksContaining(string text, string innerPattern)
    {
        // Find all '{' positions and try to match balanced closing '}'
        var result = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{')
            {
                // Find the balanced closing brace
                var depth = 1;
                var j = i + 1;
                var inString = false;
                var escape = false;
                while (j < text.Length && depth > 0)
                {
                    var c = text[j];
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (c == '\\' && inString)
                    {
                        escape = true;
                    }
                    else if (c == '"' && !escape)
                    {
                        inString = !inString;
                    }
                    else if (!inString)
                    {
                        if (c == '{') depth++;
                        else if (c == '}') depth--;
                    }
                    j++;
                }

                if (depth == 0)
                {
                    // We have a balanced block from i to j
                    var block = text[i..j];
                    if (System.Text.RegularExpressions.Regex.IsMatch(block, innerPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        // Skip this entire block — it's a leaked function definition
                        i = j;
                        continue;
                    }
                }
            }
            result.Append(text[i]);
            i++;
        }
        return result.ToString();
    }
    // --- Tool name and result formatting helpers ---

    private static string GetFriendlyToolName(string toolName) =>
        FriendlyToolNames.TryGetValue(toolName, out var friendly) ? friendly : toolName;

    private static string FormatArgsSummary(string toolName, string? args)
    {
        if (string.IsNullOrEmpty(args)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(args);
            var root = doc.RootElement;
            return toolName switch
            {
                "list_coded_value_categories" => string.Empty,
                "get_coded_value_by_code" => $"code: {ArgDisplay(root, "code")}",
                "create_coded_value" => $"{ArgDisplay(root, "code")}: {ArgDisplay(root, "name")}",
                "create_bulk_values" => $"parent: {ArgDisplay(root, "parentCode")}",
                "set_attribute_definition" => $"{ArgDisplay(root, "parentCode")}/{ArgDisplay(root, "key")}",
                "set_attribute" => $"{ArgDisplay(root, "code")}.{ArgDisplay(root, "key")} = {ArgDisplay(root, "value")}",
                _ => string.Join(", ", root.EnumerateObject().Select(p => p.Name))
            };
        }
        catch { return string.Empty; }
    }

    private static string ArgDisplay(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el) || el.ValueKind == JsonValueKind.Null) return "?";
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.ToString()
        };
    }

    private static string FormatResultSummary(string toolName, string result)
    {
        if (string.IsNullOrEmpty(result))
            return string.Empty;

        if (result.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
            return TruncateResult(result, 200);

        return toolName switch
        {
            "list_coded_value_categories" => FormatListResult(result),
            "get_coded_value_by_code" => FormatGetByCodeResult(result),
            "create_coded_value" => TruncateResult(result, 150),
            "create_bulk_values" => FormatBulkResult(result),
            "set_attribute_definition" => TruncateResult(result, 150),
            "set_attribute" => TruncateResult(result, 150),
            _ => TruncateResult(result, 150)
        };
    }

    private static string FormatListResult(string result)
    {
        if (result == "No coded value categories found.")
            return result;
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var count = lines.Length;
        return count <= 3 ? result : $"{count} categories found";
    }

    private static string FormatGetByCodeResult(string result)
    {
        if (result.StartsWith("Coded value with code"))
            return TruncateResult(result, 150);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length <= 4 ? result : TruncateResult(result, 150);
    }

    private static string FormatBulkResult(string result)
    {
        var successCount = result.Count(c => c == '✓');
        var failCount = result.Count(c => c == '✗');
        if (successCount + failCount == 0)
            return TruncateResult(result, 150);
        return failCount == 0
            ? $"{successCount} values created"
            : $"{successCount} created, {failCount} failed";
    }

    private static string TruncateResult(string result, int maxLength)
    {
        if (result.Length <= maxLength) return result;
        var firstLine = result.Split('\n')[0];
        return firstLine.Length <= maxLength
            ? firstLine + "…"
            : firstLine[..maxLength] + "…";
    }
    // --- AI Tool Functions ---

    [Description("Lists all root-level coded value categories")]
    private async Task<string> ListCategoriesAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: listing coded value categories");
        var items = await _api.GetRootValuesAsync(ct);
        if (items is null or { Length: 0 })
            return "No coded value categories found.";

        return string.Join("\n", items.Select(i =>
            $"- Code: {i.Code}, Name: {i.Name}, Children: {i.ChildrenCount}, Description: {i.Description ?? "none"}"));
    }

    [Description("Gets a coded value by its unique code")]
    private async Task<string> GetByCodeAsync(
        [Description("The unique code of the coded value, e.g. CNTRY")] string code,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: getting coded value by code {Code}", code);
        var item = await _api.GetByCodeAsync(code, ct);
        if (item is null)
            return $"Coded value with code '{code}' not found.";

        var result = $"Found: Code={item.Code}, Name={item.Name}, Id={item.Id}";
        if (item.ChildrenCount > 0)
        {
            var children = await _api.GetChildrenAsync(item.Id, ct);
            if (children is not null)
                result += $"\nChildren ({children.Length}):\n" + string.Join("\n", children.Select(c => $"  - {c.Code}: {c.Name}"));
        }

        return result;
    }

    [Description("Creates a new coded value")]
    private async Task<string> CreateCodedValueAsync(
        [Description("Unique uppercase code, e.g. CNTRY")] string code,
        [Description("Display name, e.g. Countries")] string name,
        [Description("Optional description")] string? description = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: creating coded value {Code}", code);

        var existing = await _api.GetByCodeAsync(code, ct);
        if (existing is not null)
            return $"A coded value with code '{code}' already exists: {existing.Name} (Id: {existing.Id}). Use a different code or get_coded_value_by_code to inspect it.";

        try
        {
            await _api.CreateAsync(new CreateCodedValueRequest(code, name, description, null), ct);
            return $"Created coded value: Code={code}, Name={name}";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to create coded value {Code}", code);
            return $"Error creating coded value '{code}': {ex.Message}";
        }
    }

    [Description("Creates multiple child values under a parent coded value")]
    private async Task<string> CreateBulkValuesAsync(
        [Description("The code of the parent category, e.g. CNTRY")] string parentCode,
        BulkChildItem[] children,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: creating {Count} bulk values under parent {ParentCode}", children.Length, parentCode);

        var parent = await _api.GetByCodeAsync(parentCode, ct);
        if (parent is null)
            return $"Parent coded value '{parentCode}' not found. Create it first with create_coded_value.";

        var results = new List<string>();
        foreach (var child in children)
        {
            try
            {
                await _api.CreateAsync(new CreateCodedValueRequest(
                    child.Code, child.Name, child.Description, parent.Id), ct);
                results.Add($"✓ {child.Code}: {child.Name}");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to create child value {Code} under {ParentCode}", child.Code, parentCode);
                results.Add($"✗ {child.Code}: {child.Name} — {ex.Message}");
            }
        }

        return $"Created {results.Count(r => r.StartsWith("✓"))} of {children.Length} values under '{parentCode} ({parent.Name})':\n" +
               string.Join("\n", results);
    }

    [Description("Defines an attribute on a PARENT coded value so children can set values for it")]
    private async Task<string> SetAttributeDefinitionAsync(
        string parentCode,
        string key,
        string? displayName = null,
        int dataType = 0,
        string? sourceCode = null,
        bool isRequired = false,
        bool allowMultiple = false,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: setting attribute definition '{Key}' on parent {ParentCode}", key, parentCode);

        var parent = await _api.GetByCodeAsync(parentCode, ct);
        if (parent is null)
            return $"Parent coded value '{parentCode}' not found. Create it first.";

        var existing = parent.AttributeDefinitions.FirstOrDefault(d => d.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return $"Attribute definition '{key}' already exists on '{parentCode}' with DataType={existing.DataType}.";

        var dt = (AttributeDataType)dataType;
        var req = new AttributeDefinitionRequest(displayName ?? key, dt, sourceCode, isRequired, allowMultiple);
        await _api.SetAttributeDefinitionAsync(parent.Id, key, req, ct);

        return $"Defined attribute '{key}' on parent '{parentCode}' — DataType={dt}, IsRequired={isRequired}, AllowMultiple={allowMultiple}. " +
               "Children can now set values for this attribute using set_attribute.";
    }

    [Description("Sets an attribute value on a coded value. The definition must exist on its parent.")]
    private async Task<string> SetAttributeAsync(
        string code,
        string key,
        string value,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: setting attribute '{Key}' = '{Value}' on {Code}", key, value, code);

        var item = await _api.GetByCodeAsync(code, ct);
        if (item is null)
            return $"Coded value '{code}' not found.";

        if (item.ParentId is null)
            return $"'{code}' is a root/parent coded value. Attributes are set on children, not parents. " +
                   "Use set_attribute_definition to define metadata on parents instead.";

        var parent = await _api.GetByIdAsync(item.ParentId.Value, ct);
        if (parent is null)
            return $"Parent of '{code}' not found.";

        var definition = parent.AttributeDefinitions.FirstOrDefault(d => d.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
            return $"Attribute definition '{key}' not found on parent '{parent.Code}'. " +
                   $"Call set_attribute_definition on parent '{parent.Code}' first to define this attribute.";

        await _api.SetAttributeAsync(item.Id, key, value, ct);

        return $"Set attribute '{key}' = '{value}' on '{code}' (parent: {parent.Code}).";
    }
}

// --- Chat update types for streaming UI updates ---

public abstract record ChatUpdate
{
    public sealed record TextChunk(string Text) : ChatUpdate;
    public sealed record ToolCallStart(string CallId, string FriendlyName, string ArgsSummary) : ChatUpdate;
    public sealed record ToolCallEnd(string CallId, string FriendlyName, string? ResultSummary, bool Success) : ChatUpdate;
    public sealed record Error(string Message) : ChatUpdate;
}

public record BulkChildItem(
    [Description("Short uppercase code for the child value, e.g. US")] string Code,
    [Description("Display name, e.g. United States")] string Name,
    [Description("Optional description")] string? Description = null);