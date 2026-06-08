using System.Text;
using System.Text.RegularExpressions;

namespace SchoolCollab.CodedValues.AI.Services;

/// <summary>
/// Cleans AI model output text to remove leaked internal syntax (thinking tags,
/// function-call JSON, tool names) that local LLMs frequently emit as plain text.
/// Two cleaning levels:
/// <list type="bullet">
///   <item><see cref="CleanForDisplay"/> — gentle, for UI display. Preserves prose.</item>
///   <item><see cref="CleanForHistory"/> — aggressive, for conversation history. Strips tool names.</item>
/// </list>
/// </summary>
internal static class AiTextCleaner
{
    /// <summary>
    /// All known AI tool names as a regex alternation group.
    /// </summary>
    internal static readonly string ToolNameRegexPattern =
        @"(?:list_coded_value_categories|get_coded_value_by_code|create_coded_value|create_bulk_values|update_coded_value|disable_coded_value|enable_coded_value|set_attribute_definition|set_attribute)";

    /// <summary>
    /// Aggressive cleaning for text added to conversation history. Strips tool names,
    /// tool-narration patterns, and anything that could cause the model to echo
    /// leaked syntax back in subsequent rounds.
    /// </summary>
    internal static string CleanForHistory(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Apply display-level cleaning first
        text = CleanForDisplay(text);

        // Remove ANY line containing a known tool name anywhere on the line.
        text = Regex.Replace(
            text,
            $@"^.*\b{ToolNameRegexPattern}\b.*\r?\n?",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // Remove lines that narrate tool usage even without exact tool names.
        text = Regex.Replace(
            text,
            @"^\s*(?:I(?:'ll| will| can| should|'m going to)?\s+(?:use|call|invoke|run|execute)\s+(?:the\s+)?(?:tool|function|method|API)\b|Let\s+me\s+(?:use|call|invoke|run)\b|Now\s+(?:I\s+)?(?:will\s+)?(?:use|call|invoke)\b|I'm\s+(?:going\s+to\s+)?(?:use|call|invoke)\b)[^\r\n]*\r?\n?",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // Collapse excessive blank lines
        text = Regex.Replace(text, @"(\r?\n){2,}", "\n");

        return text.Trim();
    }

    /// <summary>
    /// Lighter cleaning for text shown to the user in the UI. Strips actual leaked
    /// syntax (thinking tags, function-def JSON, raw tool-call-as-text) but preserves
    /// normal prose that may mention capability names in a helpful context.
    /// </summary>
    internal static string CleanForDisplay(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove model-internal thinking/scratchpad tags
        text = Regex.Replace(text, @"<thinking>[\s\S]*?</thinking>", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<scratchpad>[\s\S]*?</scratchpad>", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<reflection>[\s\S]*?</reflection>", string.Empty, RegexOptions.IgnoreCase);

        // Remove function/tool definition JSON that local LLMs leak as text.
        text = RemoveJsonBlocksContaining(text, "\"type\"\\s*:\\s*\"function\"");
        text = RemoveJsonBlocksContaining(text, "'type'\\s*:\\s*'function'");
        text = RemoveJsonBlocksContaining(text, "\"function\"\\s*:\\s*\\{");

        // Remove multi-line function-call syntax blocks that aren't JSON format.
        text = Regex.Replace(
            text,
            $@"{ToolNameRegexPattern}\s*\([\s\S]*?\)\s*[;,]?",
            string.Empty,
            RegexOptions.IgnoreCase);

        // Remove lines that are just tool names (standalone or with arrows/prefixes)
        text = Regex.Replace(
            text,
            $@"^\s*(?:→|->|≫|>>|▸|•)?\s*{ToolNameRegexPattern}\s*(?:\([^)]*\))?\s*[;,]?\s*\r?\n?",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // Remove lines that are just 'name': 'tool_name' or "name": "tool_name"
        text = Regex.Replace(
            text,
            $@"^\s*['""]name['""]\s*:\s*['""]{ToolNameRegexPattern}['""].*\r?\n?",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // Remove empty JSON objects/tags: {}, { }, {  }, etc.
        text = Regex.Replace(text, @"\{\s*\}", string.Empty);

        // Remove remaining raw JSON data blocks the model emits as text.
        text = RemoveJsonBlocksContaining(text, @"""(?:id|code|parentId|description|displayOrder|isDisabled|isDeleted)""\s*:");

        // Normalize line endings to \n for consistent output
        text = text.Replace("\r\n", "\n");

        // Collapse excessive blank lines (2+ consecutive → single)
        text = Regex.Replace(text, @"\n{2,}", "\n");

        var result = text.Trim();

        // Suppress if remaining text contains no alphabetic characters (just punctuation/symbols)
        if (result.Length > 0 && !result.Any(char.IsLetter))
            return string.Empty;

        return result;
    }

    /// <summary>
    /// Removes JSON object blocks (balanced braces) that contain a specific pattern.
    /// Handles nested braces correctly for multi-line JSON.
    /// </summary>
    internal static string RemoveJsonBlocksContaining(string text, string innerPattern)
    {
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
                    var block = text[i..j];
                    if (Regex.IsMatch(block, innerPattern, RegexOptions.IgnoreCase))
                    {
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
}