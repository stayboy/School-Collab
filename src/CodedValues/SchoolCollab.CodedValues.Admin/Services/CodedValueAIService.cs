using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
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

    private readonly List<AITool> _tools;

    public CodedValueAIService(IChatClient chatClient, CodedValuesApiClient api, ILogger<CodedValueAIService> logger)
    {
        _chatClient = chatClient;
        _api = api;
        _logger = logger;

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

    private const string SystemPrompt = """
        You are a helpful assistant for managing coded values in a school collaboration system.
        Coded values are hierarchical lookup tables. Each has a unique code, a name, and an optional description.
        Parents define categories; children are the actual values.

        ## REQUIRED WORKFLOW — follow these steps in order:

        ### Step 1: Ask for the parent code (REQUIRED)
        Before creating any coded value, you MUST ask the user which parent category to use:
        - If they specify one (e.g., "add to CNTRY"), use it.
        - If they say "create a new one", ask for the code and name for the parent.
        - Use list_coded_value_categories or get_coded_value_by_code to check if it exists.
        NEVER create a coded value without first confirming the parent code.

        ### Step 2: Ask for code and description (REQUIRED)
        For each coded value (parent or child), you MUST ask for:
        - **Code**: Short uppercase identifier (e.g., "US", "CNTRY")
        - **Description**: A brief description or machine-readable value
        These are required. Do not proceed without them.

        ### Step 3: Ask about attributes (OPTIONAL)
        After collecting code and description, ask: "Would you like to add any attribute values for these coded values?"
        If they say yes, ask which attributes they want to set.
        If they say no, skip to Step 4.

        ### Step 4: Define attribute definitions on the PARENT (if attributes are needed)
        If the user wants attributes on children, check if the parent already has those attribute definitions.
        Use get_coded_value_by_code to inspect the parent's attributeDefinitions.
        If a definition doesn't exist, call set_attribute_definition on the PARENT code.
        When inferring the data type from the user's description:
        - Numbers, prices, weights → Decimal (2)
        - Whole numbers → Integer (1)
        - True/false flags → Boolean (3)
        - Dates → Date (4)
        - Times → Time (6)
        - References to another coded value category → CodedValue (7), and set sourceCode to that category's code
        - Anything else → Text (0) (DEFAULT)
        Default to Text (0) when uncertain.

        ### Step 5: Create coded values and set attributes
        Create the parent (if new) and children.
        If attributes are needed, call set_attribute on each child with the value.

        ## Important rules:
        - Attribute definitions live on PARENTS. Attribute values live on CHILDREN.
        - A definition must exist on the parent before values can be set on children.
        - Always confirm what you're going to create before making API calls.
        - Report results clearly after each operation.
        """;

    /// <summary>
    /// Sends conversation history to the AI and returns the response as a stream of text chunks.
    /// The AI can call the registered tools to create/list coded values.
    /// </summary>
    public async IAsyncEnumerable<string> ChatAsync(
        IReadOnlyList<ChatMessage> history,
        string? model = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Processing AI chat with {Count} history messages", history.Count);

        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };
        messages.AddRange(history);

        var options = model is not null
            ? new ChatOptions { Tools = _tools, ModelId = model }
            : new ChatOptions { Tools = _tools };

        var result = _chatClient.GetStreamingResponseAsync(messages, options, ct);

        var fullResponse = new StringBuilder();
        await foreach (var chunk in result.WithCancellation(ct))
        {
            if (chunk.Text is not null)
            {
                fullResponse.Append(chunk.Text);
                yield return chunk.Text;
            }
        }

        _logger.LogInformation("AI chat completed with {Length} chars", fullResponse.Length);
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
        [Description("Optional parent ID for creating a child value")] Guid? parentId = null,
        [Description("Optional display order")] int displayOrder = 0,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: creating coded value {Code}", code);

        // Check for duplicate code first
        var existing = await _api.GetByCodeAsync(code, ct);
        if (existing is not null)
            return $"A coded value with code '{code}' already exists: {existing.Name} (Id: {existing.Id}). Use a different code or get_coded_value_by_code to inspect it.";

        try
        {
            await _api.CreateAsync(new CreateCodedValueRequest(code, name, description, parentId, displayOrder), ct);
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
        [Description("Array of child items to create, each with code and name")] BulkChildItem[] children,
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
                    child.Code, child.Name, child.Description, parent.Id, child.DisplayOrder), ct);
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
        [Description("The code of the PARENT coded value to define the attribute on, e.g. AI-MODELS")] string parentCode,
        [Description("Unique key for the attribute, e.g. 'weight' or 'color'")] string key,
        [Description("Human-readable label, e.g. 'Weight' or 'Color'")] string? displayName = null,
        [Description("Data type: 0=Text, 1=Integer, 2=Decimal, 3=Boolean, 4=Date, 5=DateTime, 6=Time, 7=CodedValue. Default is 0 (Text).")] int dataType = 0,
        [Description("If dataType=7 (CodedValue), the code of another parent to use as dropdown values")] string? sourceCode = null,
        [Description("Whether children must set this attribute")] bool isRequired = false,
        [Description("Whether children can have multiple values for this attribute")] bool allowMultiple = false,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: setting attribute definition '{Key}' on parent {ParentCode}", key, parentCode);

        var parent = await _api.GetByCodeAsync(parentCode, ct);
        if (parent is null)
            return $"Parent coded value '{parentCode}' not found. Create it first.";

        // Check if definition already exists
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
        [Description("The code of the coded value to set the attribute on")] string code,
        [Description("The attribute key that matches a definition on the parent")] string key,
        [Description("The value as a string")] string value,
        CancellationToken ct = default)
    {
        _logger.LogDebug("AI tool: setting attribute '{Key}' = '{Value}' on {Code}", key, value, code);

        var item = await _api.GetByCodeAsync(code, ct);
        if (item is null)
            return $"Coded value '{code}' not found.";

        // Verify the parent has the definition
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

/// <summary>
/// A child item for bulk creation.
/// </summary>
public record BulkChildItem(
    [Description("Short uppercase code for the child value, e.g. US")] string Code,
    [Description("Display name, e.g. United States")] string Name,
    [Description("Optional description")] string? Description = null,
    [Description("Optional display order")] int DisplayOrder = 0);