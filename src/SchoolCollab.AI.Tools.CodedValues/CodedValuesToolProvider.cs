using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SchoolCollab.AI.Abstractions;

namespace SchoolCollab.AI.Tools.CodedValues;

/// <summary>
/// CodedValues-flavoured <see cref="IToolProvider"/>: exposes the 9 coded-value
/// tools to the AI model, narrows the tool bag per prompt via
/// <see cref="SelectToolsForPrompt"/> (the intent classifier carried over
/// verbatim from the former <c>CodedValueAIService</c>), dispatches tool calls
/// to the Coded Values REST API through <see cref="ICodedValuesApiClient"/>,
/// and supplies the friendly-name / args-summary / result-summary formatting
/// that the engine emits in the SSE <c>ToolCallStart</c>/<c>ToolCallEnd</c>
/// events (kept byte-for-byte identical to the pre-refactor payload).
/// </summary>
public sealed class CodedValuesToolProvider : IToolProvider
{
    private readonly ICodedValuesApiClient _api;
    private readonly ILogger<CodedValuesToolProvider> _logger;

    private readonly List<AITool> _tools;
    private readonly Dictionary<string, AITool> _toolsByName;

    private static readonly Dictionary<string, string> FriendlyToolNames = new()
    {
        ["list_coded_value_categories"] = "List Categories",
        ["get_coded_value_by_code"] = "Get By Code",
        ["create_coded_value"] = "Create Value",
        ["create_bulk_values"] = "Create Bulk Values",
        ["update_coded_value"] = "Update Value",
        ["disable_coded_value"] = "Disable Value",
        ["enable_coded_value"] = "Enable Value",
        ["set_attribute_definition"] = "Define Attribute",
        ["set_attribute"] = "Set Attribute"
    };

    public IReadOnlyList<string> ToolNames { get; }

    public CodedValuesToolProvider(ICodedValuesApiClient api, ILogger<CodedValuesToolProvider> logger)
    {
        _api = api;
        _logger = logger;

        _tools =
        [
            AIFunctionFactory.Create(ListCategoriesAsync, "list_coded_value_categories", "Lists all root-level coded value categories with their code, name, children count, and description. Use this when the user refers to a category by name but not by code — find the matching code here, then use get_coded_value_by_code to retrieve full details."),
            AIFunctionFactory.Create(GetByCodeAsync, "get_coded_value_by_code", "Gets a coded value by its unique code. Returns ALL current fields (name, description, display order, disabled status, attributes, and children with their details). Use this to look up a value before updating it so you know the current state of every field."),
            AIFunctionFactory.Create(CreateCodedValueAsync, "create_coded_value", "Creates a new coded value. Use parentCode to create a child under an existing parent, or omit it to create a root-level category. Accepts code, name, optional description, optional parentCode, and optional displayOrder."),
            AIFunctionFactory.Create(CreateBulkValuesAsync, "create_bulk_values", "Creates multiple child values under a parent coded value in one call. Accepts parentCode (the code of the parent category) and an array of child items, each with code, name, description (strongly recommended — provide a concise description for each child when one can be reasonably inferred), and optional displayOrder. Use this to populate a category with many values at once, e.g., countries under a Countries category."),
            AIFunctionFactory.Create(UpdateCodedValueAsync, "update_coded_value", "Updates an existing coded value. Accepts the code of the value to update. Any fields you provide will be changed; fields you omit will keep their current values. Always call get_coded_value_by_code first to see the current state, then call this with only the fields you want to change."),
            AIFunctionFactory.Create(DisableCodedValueAsync, "disable_coded_value", "Disables a coded value so it no longer appears in active selections. Accepts the code of the value to disable."),
            AIFunctionFactory.Create(EnableCodedValueAsync, "enable_coded_value", "Re-enables a previously disabled coded value. Accepts the code of the value to enable."),
            AIFunctionFactory.Create(SetAttributeDefinitionAsync, "set_attribute_definition", "Defines an attribute on a PARENT coded value. Attribute definitions describe what metadata children should have. Must be called on the parent before setting attribute values on children. Accepts: parentCode (the parent's code, e.g. AI-MODELS), key (attribute key like 'weight'), displayName (human-readable label), dataType (0=Text,1=Integer,2=Decimal,3=Boolean,4=Date,5=DateTime,6=Time,7=CodedValue), sourceCode (required only if dataType=7, references another parent code for dropdown values), isRequired, allowMultiple."),
            AIFunctionFactory.Create(SetAttributeAsync, "set_attribute", "Sets an attribute value on a coded value. The attribute definition with the same key must already exist on the PARENT of this coded value. Accepts: code (the coded value's code), key (attribute key that matches a definition on the parent), value (the value as a string).")
        ];

        _toolsByName = _tools.ToDictionary(t => t.Name);
        ToolNames = _tools.Select(t => t.Name).ToList();
    }

    /// <summary>
    /// Returns the per-turn tool subset for the user's most recent prompt,
    /// applying the intent classifier carried over verbatim from the former
    /// <c>CodedValueAIService</c>. The engine calls this each turn so the model
    /// sees the same filtered tool bag it saw before the refactor.
    /// </summary>
    public IReadOnlyList<AITool> CreateTools(IReadOnlyList<ChatMessage> history, ILogger logger) =>
        SelectToolsForPrompt(history);

    public Task<string> DispatchAsync(string toolName, string? args, CancellationToken ct) =>
        DispatchToolCallAsync(toolName, args, ct);

    public string GetFriendlyName(string toolName) =>
        FriendlyToolNames.TryGetValue(toolName, out var friendly) ? friendly : toolName;

    /// <summary>
    /// Classifies the user's most recent prompt into a small set of intents
    /// and returns the <see cref="AITool"/>s that are relevant to that intent.
    /// Defaults to the full tool list when the prompt doesn't match any intent
    /// keyword — preserving the prior "ship everything" behaviour as the
    /// safety fallback. The classification is intentionally conservative:
    /// if there's any doubt, ship everything.
    /// </summary>
    internal List<AITool> SelectToolsForPrompt(IReadOnlyList<ChatMessage> history)
    {
        // Find the most recent user message. If none, fall back to all tools.
        var latestUser = null as string;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Role == ChatRole.User)
            {
                latestUser = history[i].Text;
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(latestUser))
            return _tools;

        var text = latestUser.ToLowerInvariant();

        // Order matters: check the most specific intent first.
        if (ContainsAny(text, "disable", "enable", "hide", "deactivate", "archive", "reactivate"))
            return GetTools("list_coded_value_categories", "get_coded_value_by_code", "disable_coded_value", "enable_coded_value");

        if (ContainsAny(text, "update", "rename", "change ", "set description", "set the description", "reorder", "move"))
            return GetTools("list_coded_value_categories", "get_coded_value_by_code", "update_coded_value");

        if (ContainsAny(text, "add ", "create ", "import ", "populate", "propose", "set up", "load ", "new categories", "countries", "languages", "subjects", "genres"))
            return GetTools("list_coded_value_categories", "get_coded_value_by_code", "create_coded_value", "create_bulk_values", "set_attribute_definition", "set_attribute");

        // Read-only / browse / list / show — never write tools needed.
        if (ContainsAny(text, "list", "show ", "find ", "what", "which", "see ", "look up", "search"))
            return GetTools("list_coded_value_categories", "get_coded_value_by_code");

        // Default: ship everything. This is the safe fallback for prompts we
        // don't recognise (e.g. "hello", or anything ambiguous).
        return _tools;

        // Local helper — match any of the intent keywords against the prompt.
        bool ContainsAny(string haystack, params string[] needles) =>
            needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

        // Local helper — collect tools by name into a fresh list.
        List<AITool> GetTools(params string[] toolNames)
        {
            var list = new List<AITool>(toolNames.Length);
            foreach (var name in toolNames)
                if (_toolsByName.TryGetValue(name, out var tool))
                    list.Add(tool);
            return list;
        }
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
                "update_coded_value" => await DispatchUpdateCodedValueAsync(arguments, ct),
                "disable_coded_value" => await DispatchDisableCodedValueAsync(arguments, ct),
                "enable_coded_value" => await DispatchEnableCodedValueAsync(arguments, ct),
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
        var parentCode = ExtractStringArg(args, "parentCode");
        var displayOrder = ExtractIntArg(args, "displayOrder") ?? 0;
        return await CreateCodedValueAsync(code, name, description, parentCode, displayOrder, ct);
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

    private async Task<string> DispatchUpdateCodedValueAsync(string? args, CancellationToken ct)
    {
        var code = ExtractStringArg(args, "code") ?? "";
        var name = ExtractStringArg(args, "name");
        var description = ExtractStringArg(args, "description");
        var displayOrder = ExtractIntArg(args, "displayOrder");
        return await UpdateCodedValueAsync(code, name, description, displayOrder, ct);
    }

    private async Task<string> DispatchDisableCodedValueAsync(string? args, CancellationToken ct)
    {
        var code = ExtractStringArg(args, "code") ?? "";
        return await DisableCodedValueAsync(code, ct);
    }

    private async Task<string> DispatchEnableCodedValueAsync(string? args, CancellationToken ct)
    {
        var code = ExtractStringArg(args, "code") ?? "";
        return await EnableCodedValueAsync(code, ct);
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
                el.TryGetProperty("description", out var d) ? d.GetString() : null,
                el.TryGetProperty("displayOrder", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : 0
            )).ToArray();
        }
        catch { return []; }
    }

    // --- SSE formatting (kept byte-for-byte identical to the pre-refactor payload) ---

    public string FormatArgsSummary(string toolName, string? args)
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
                "update_coded_value" => $"{ArgDisplay(root, "code")}: {ArgDisplay(root, "name")}",
                "disable_coded_value" => $"{ArgDisplay(root, "code")}",
                "enable_coded_value" => $"{ArgDisplay(root, "code")}",
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

    public string FormatResultSummary(string toolName, string result)
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
            "update_coded_value" => TruncateResult(result, 150),
            "disable_coded_value" => TruncateResult(result, 150),
            "enable_coded_value" => TruncateResult(result, 150),
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

    [Description("Lists all root-level coded value categories with their code, name, children count, and description. Use this to find the code for a category when the user provides only a name.")]
    private async Task<string> ListCategoriesAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: listing coded value categories");
        var items = await _api.GetRootValuesAsync(ct);
        if (items is null or { Length: 0 })
            return "No coded value categories found.";

        return string.Join("\n", items.Select(i =>
            $"- Code: {i.Code}, Name: {i.Name}, Children: {i.ChildrenCount}, Description: {i.Description ?? "none"}"));
    }

    [Description("Gets a coded value by its unique code. Returns ALL current fields including name, description, display order, disabled status, and attributes. Use this to look up a value before updating it.")]
    private async Task<string> GetByCodeAsync(
        [Description("The unique code of the coded value, e.g. CNTRY")] string code,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: getting coded value by code {Code}", code);
        var item = await _api.GetByCodeAsync(code, ct: ct);
        if (item is null)
            return $"Coded value with code '{code}' not found.";

        var result = $"Found: Code={item.Code}, Name={item.Name}, Id={item.Id}, " +
                     $"Description={item.Description ?? "(none)"}, DisplayOrder={item.DisplayOrder}, IsDisabled={item.IsDisabled}";

        if (item.AttributeDefinitions is { Count: > 0 })
        {
            result += $"\nAttributeDefinitions ({item.AttributeDefinitions.Count}):\n" +
                      string.Join("\n", item.AttributeDefinitions.Select(d =>
                          $"  - {d.Key}: DisplayName={d.DisplayName ?? d.Key}, DataType={d.DataType}, IsRequired={d.IsRequired}, AllowMultiple={d.AllowMultiple}"));
        }

        if (item.Attributes is { Count: > 0 })
        {
            result += $"\nAttributes ({item.Attributes.Count}):\n" +
                      string.Join("\n", item.Attributes.Select(a =>
                          $"  - {a.Key}={a.Value}"));
        }

        if (item.ChildrenCount > 0)
        {
            var children = await _api.GetChildrenAsync(item.Id, ct);
            if (children is not null)
            {
                result += $"\nChildren ({children.Length}):\n" + string.Join("\n", children.Select(c =>
                    $"  - {c.Code}: {c.Name}, Description={c.Description ?? "(none)"}, DisplayOrder={c.DisplayOrder}, IsDisabled={c.IsDisabled}" +
                    (c.Attributes is { Count: > 0 } ? ", Attributes=[" + string.Join(", ", c.Attributes.Select(a => $"{a.Key}={a.Value}")) + "]" : "")));
            }
        }

        return result;
    }

    [Description("Creates a new coded value. Use parentCode to create a child under an existing parent, or omit it to create a root-level category.")]
    private async Task<string> CreateCodedValueAsync(
        [Description("Unique uppercase code, e.g. CNTRY")] string code,
        [Description("Display name, e.g. Countries")] string name,
        [Description("Optional description")] string? description = null,
        [Description("Optional code of the parent category to create this value under, e.g. CNTRY")] string? parentCode = null,
        [Description("Sort order for display, starting from 1")] int displayOrder = 0,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: creating coded value {Code}", code);

        Guid? parentId = null;
        if (!string.IsNullOrEmpty(parentCode))
        {
            var parent = await _api.GetByCodeAsync(parentCode, ct: ct);
            if (parent is null)
                return $"Parent coded value '{parentCode}' not found. Create it first or use a valid parent code.";
            parentId = parent.Id;
        }

        var existing = await _api.GetByCodeAsync(code, parentId, ct);
        if (existing is not null)
            return $"A coded value with code '{code}' already exists: {existing.Name} (Id: {existing.Id}). Use a different code or get_coded_value_by_code to inspect it.";

        try
        {
            await _api.CreateAsync(new CreateCodedValueRequest(code, name, description, parentId, displayOrder), ct);
            return parentId.HasValue
                ? $"Created child value: Code={code}, Name={name}, Description={description ?? "(none)"} under parent {parentCode}"
                : $"Created root category: Code={code}, Name={name}, Description={description ?? "(none)"}";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to create coded value {Code}", code);
            return $"Error creating coded value '{code}': {ex.Message}";
        }
    }

    [Description("Creates multiple child values under a parent coded value in one call. Accepts parentCode (the code of the parent category) and an array of child items, each with code, name, optional description, and optional displayOrder. Use this to populate a category with many values at once, e.g., countries under a Countries category.")]
    private async Task<string> CreateBulkValuesAsync(
        [Description("The code of the parent category, e.g. CNTRY")] string parentCode,
        BulkChildItem[] children,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: creating {Count} bulk values under parent {ParentCode}", children.Length, parentCode);

        var parent = await _api.GetByCodeAsync(parentCode, ct: ct);
        if (parent is null)
            return $"Parent coded value '{parentCode}' not found. Create it first with create_coded_value.";

        try
        {
            var requests = children.Select((child, i) =>
            {
                var displayOrder = child.DisplayOrder > 0 ? child.DisplayOrder : i + 1;
                return new CreateCodedValueRequest(child.Code, child.Name, child.Description, parent.Id, displayOrder);
            });

            var result = await _api.BulkCreateAsync(parent.Id, requests, ct);
            var skippedInfo = result.SkippedCodes.Count > 0
                ? $" Skipped existing codes: {string.Join(", ", result.SkippedCodes)}."
                : "";
            var childDescs = string.Join(", ", children.Select(c =>
                string.IsNullOrEmpty(c.Description) ? $"{c.Code}" : $"{c.Code}: {c.Description}"));
            return $"Created {result.CreatedCount} value{(result.CreatedCount == 1 ? "" : "s")} under '{parentCode} ({parent.Name})'.{skippedInfo} Descriptions: {childDescs}";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to bulk create values under {ParentCode}", parentCode);
            return $"Failed to create values under '{parentCode}': {ex.Message}";
        }
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

        var parent = await _api.GetByCodeAsync(parentCode, ct: ct);
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

    [Description("Updates an existing coded value. Only the fields you specify will be changed — any field left null keeps its current value. Always call get_coded_value_by_code first to see the current state before updating.")]
    private async Task<string> UpdateCodedValueAsync(
        [Description("The unique code of the coded value to update")] string code,
        [Description("New display name. Leave null to keep the current name.")] string? name = null,
        [Description("New description. Leave null to keep the current description.")] string? description = null,
        [Description("New display order position. Leave null to keep the current order.")] int? displayOrder = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: updating coded value {Code}", code);

        var item = await _api.GetByCodeAsync(code, ct: ct);
        if (item is null)
            return $"Coded value '{code}' not found.";

        var newName = name ?? item.Name;
        var newDesc = description ?? item.Description;
        var newOrder = displayOrder ?? item.DisplayOrder;

        await _api.UpdateAsync(item.Id, new UpdateCodedValueRequest(newName, newDesc, newOrder), ct);
        return $"Updated '{code}': Name={newName}, Description={newDesc ?? "(none)"}, DisplayOrder={newOrder}";
    }

    [Description("Disables a coded value so it no longer appears in active selections")]
    private async Task<string> DisableCodedValueAsync(
        [Description("The unique code of the coded value to disable")] string code,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: disabling coded value {Code}", code);

        var item = await _api.GetByCodeAsync(code, ct: ct);
        if (item is null)
            return $"Coded value '{code}' not found.";
        if (item.IsDisabled)
            return $"'{code}' is already disabled.";

        await _api.DisableAsync(item.Id, ct);
        return $"Disabled '{code} ({item.Name})'. It will no longer appear in active selections.";
    }

    [Description("Re-enables a previously disabled coded value")]
    private async Task<string> EnableCodedValueAsync(
        [Description("The unique code of the coded value to enable")] string code,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: enabling coded value {Code}", code);

        var item = await _api.GetByCodeAsync(code, ct: ct);
        if (item is null)
            return $"Coded value '{code}' not found.";
        if (!item.IsDisabled)
            return $"'{code}' is already enabled.";

        await _api.EnableAsync(item.Id, ct);
        return $"Enabled '{code} ({item.Name})'. It is now available in active selections.";
    }

    [Description("Sets an attribute value on a coded value. The definition must exist on its parent.")]
    private async Task<string> SetAttributeAsync(
        string code,
        string key,
        string value,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: setting attribute '{Key}' = '{Value}' on {Code}", key, value, code);

        var item = await _api.GetByCodeAsync(code, ct: ct);
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