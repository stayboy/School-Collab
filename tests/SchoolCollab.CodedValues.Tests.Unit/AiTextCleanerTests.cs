using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.AI.Services;

namespace SchoolCollab.CodedValues.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="AiTextCleaner"/> covering both
/// <see cref="AiTextCleaner.CleanForDisplay"/> and <see cref="AiTextCleaner.CleanForHistory"/>.
/// </summary>
[TestClass]
public class AiTextCleanerTests
{
    // =====================================================================
    // CleanForDisplay — thinking/scratchpad/reflection tags
    // =====================================================================

    [TestMethod]
    public void CleanForDisplay_StripsThinkingTags()
    {
        var input = "Hello <thinking>let me think about this</thinking> World";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Hello  World");
    }

    [TestMethod]
    public void CleanForDisplay_StripsMultilineThinkingTags()
    {
        var input = "Result:\n<thinking>\nstep 1\nstep 2\n</thinking>\nDone.";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Result:\nDone.");
    }

    [TestMethod]
    public void CleanForDisplay_StripsCaseInsensitiveThinkingTags()
    {
        var input = "Hello <Thinking>secret</Thinking> World";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Hello  World");
    }

    [TestMethod]
    public void CleanForDisplay_StripsScratchpadTags()
    {
        var input = "Hi <scratchpad>internal notes</scratchpad> there";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Hi  there");
    }

    [TestMethod]
    public void CleanForDisplay_StripsReflectionTags()
    {
        var input = "Ok <reflection>hmm</reflection> sure";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Ok  sure");
    }

    // =====================================================================
    // CleanForDisplay — function definition JSON
    // =====================================================================

    [TestMethod]
    public void CleanForDisplay_StripsFunctionDefJsonWithTypeFunction()
    {
        var input = """Here is a tool: {"type": "function", "name": "foo", "parameters": {}} and done.""";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Here is a tool:  and done.");
    }

    [TestMethod]
    public void CleanForDisplay_StripsFunctionDefJsonWithFunctionKey()
    {
        var input = """Result: {"function": {"name": "bar"}} end.""";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Result:  end.");
    }

    [TestMethod]
    public void CleanForDisplay_StripsSingleQuotedTypeFunction()
    {
        var input = "Before {'type': 'function', 'name': 'baz'} after.";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Before  after.");
    }

    [TestMethod]
    public void CleanForDisplay_StripsMultilineFunctionDefJson()
    {
        var input = """
            Calling:
            {
              "type": "function",
              "function": {
                "name": "create_coded_value"
              }
            }
            Done!
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Be("Calling:\nDone!");
    }

    // =====================================================================
    // CleanForDisplay — multi-line function-call syntax (non-JSON)
    // =====================================================================

    [TestMethod]
    public void CleanForDisplay_StripsFunctionCallSyntaxBlock()
    {
        var input = """
            Let me do this:
            create_coded_value(
              code="US",
              name="United States"
            )
            That should work.
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Be("Let me do this:\nThat should work.");
    }

    [TestMethod]
    public void CleanForDisplay_StripsSingleLineFunctionCallSyntax()
    {
        var input = "Result: get_coded_value_by_code(code=\"US\"); done.";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Result:  done.");
    }

    // =====================================================================
    // CleanForDisplay — standalone tool name lines (arrows, prefixes)
    // =====================================================================

    [TestMethod]
    public void CleanForDisplay_StripsStandaloneToolNameLine()
    {
        var input = "Before\ncreate_coded_value\nAfter";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Before\nAfter");
    }

    [TestMethod]
    public void CleanForDisplay_StripsArrowPrefixedToolNameLine()
    {
        var input = "Before\n→ create_coded_value\nAfter";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Before\nAfter");
    }

    [TestMethod]
    public void CleanForDisplay_StripsDashArrowToolNameLine()
    {
        var input = "Before\n-> get_coded_value_by_code\nAfter";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Before\nAfter");
    }

    [TestMethod]
    public void CleanForDisplay_StripsToolNameWithTrailingSemicolon()
    {
        var input = "Before\nlist_coded_value_categories;\nAfter";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Before\nAfter");
    }

    // =====================================================================
    // CleanForDisplay — "name": "tool_name" lines
    // =====================================================================

    [TestMethod]
    public void CleanForDisplay_StripsNameValueToolNameLine()
    {
        var input = "Before\n\"name\": \"create_coded_value\"\nAfter";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Before\nAfter");
    }

    // =====================================================================
    // CleanForDisplay — empty JSON objects
    // =====================================================================

    [TestMethod]
    public void CleanForDisplay_StripsEmptyJsonObjects()
    {
        var input = "Before {} after";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Before  after");
    }

    [TestMethod]
    public void CleanForDisplay_StripsWhitespaceOnlyJsonObjects()
    {
        var input = "Before {   } after";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Before  after");
    }

    // =====================================================================
    // CleanForDisplay — raw JSON data blocks with coded-value keys
    // =====================================================================

    [TestMethod]
    public void CleanForDisplay_StripsJsonDataWithCodeKey()
    {
        var input = """Result: {"code": "US", "name": "United States"} done.""";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Result:  done.");
    }

    [TestMethod]
    public void CleanForDisplay_StripsJsonDataWithIdKey()
    {
        var input = """Here: {"id": "abc-123", "name": "Test"} end.""";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Here:  end.");
    }

    [TestMethod]
    public void CleanForDisplay_StripsMultilineJsonDataBlock()
    {
        var input = """
            Created:
            {
              "code": "US",
              "name": "United States",
              "description": "Country"
            }
            All done!
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Be("Created:\nAll done!");
    }

    [TestMethod]
    public void CleanForDisplay_StripsNestedJsonDataBlock()
    {
        var input = """Data: {"id": "1", "children": [{"code": "US"}]} end.""";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Data:  end.");
    }

    [TestMethod]
    public void CleanForDisplay_PreservesJsonWithoutCodedValueKeys()
    {
        // JSON with unrelated keys should NOT be stripped
        var input = """Settings: {"theme": "dark", "language": "en"} saved.""";
        AiTextCleaner.CleanForDisplay(input).Should().Be("""Settings: {"theme": "dark", "language": "en"} saved.""");
    }

    // =====================================================================
    // CleanForDisplay — preserves human-readable prose
    // =====================================================================

    [TestMethod]
    public void CleanForDisplay_PreservesSimpleProse()
    {
        var input = "I created 3 new country values for you.";
        AiTextCleaner.CleanForDisplay(input).Should().Be("I created 3 new country values for you.");
    }

    [TestMethod]
    public void CleanForDisplay_PreservesProseWithBulletedList()
    {
        var input = """
            Here are the values I created:
            - US: United States
            - CA: Canada
            - MX: Mexico
            All done!
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("Here are the values I created:");
        result.Should().Contain("- US: United States");
        result.Should().Contain("- CA: Canada");
        result.Should().Contain("- MX: Mexico");
        result.Should().Contain("All done!");
    }

    [TestMethod]
    public void CleanForDisplay_PreservesProseMentioningCapabilities()
    {
        // The model should be able to say "I created values" without tool names
        var input = "I used the bulk creation capability to add 5 country values under COUNTRY-TYPE.";
        AiTextCleaner.CleanForDisplay(input).Should().Be(input);
    }

    [TestMethod]
    public void CleanForDisplay_PreservesProseWithMarkdownFormatting()
    {
        var input = "Created **3 values**:\n- US\n- CA\n- MX";
        AiTextCleaner.CleanForDisplay(input).Should().Be(input);
    }

    [TestMethod]
    public void CleanForDisplay_PreservesSummaryAfterToolUse()
    {
        // This is the key scenario that was broken — the model's final summary
        // mentioning what it did should NOT be stripped by CleanForDisplay
        var input = """
            I've created the following values under COUNTRY-TYPE:
            - US: United States
            - CA: Canada
            - MX: Mexico

            All 3 values are now available.
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("United States");
        result.Should().Contain("Canada");
        result.Should().Contain("All 3 values");
    }

    // =====================================================================
    // CleanForDisplay — edge cases
    // =====================================================================

    [TestMethod]
    public void CleanForDisplay_ReturnsEmptyForWhitespaceInput()
    {
        AiTextCleaner.CleanForDisplay("   \n\t  ").Should().BeEmpty();
    }

    [TestMethod]
    public void CleanForDisplay_ReturnsEmptyForNullInput()
    {
        AiTextCleaner.CleanForDisplay(null!).Should().BeEmpty();
    }

    [TestMethod]
    public void CleanForDisplay_ReturnsEmptyForEmptyInput()
    {
        AiTextCleaner.CleanForDisplay("").Should().BeEmpty();
    }

    [TestMethod]
    public void CleanForDisplay_SuppressesPunctuationOnlyFragments()
    {
        // Fragments with no alphabetic characters are suppressed
        AiTextCleaner.CleanForDisplay("...}").Should().BeEmpty();
        AiTextCleaner.CleanForDisplay("---").Should().BeEmpty();
    }

    [TestMethod]
    public void CleanForDisplay_PreservesShortWordsWithLetters()
    {
        // Short text with alphabetic characters is kept
        AiTextCleaner.CleanForDisplay("ok").Should().Be("ok");
        AiTextCleaner.CleanForDisplay("Done!").Should().Be("Done!");
    }

    [TestMethod]
    public void CleanForDisplay_CollapsesExcessiveBlankLines()
    {
        var input = "Line1\n\n\n\n\nLine2";
        AiTextCleaner.CleanForDisplay(input).Should().Be("Line1\nLine2");
    }

    // =====================================================================
    // CleanForHistory — everything from CleanForDisplay PLUS tool name stripping
    // =====================================================================

    [TestMethod]
    public void CleanForHistory_StripsLineContainingToolName()
    {
        var input = "I used create_coded_value to make a value.";
        AiTextCleaner.CleanForHistory(input).Should().BeEmpty();
    }

    [TestMethod]
    public void CleanForHistory_StripsLineWithToolNameInBackticks()
    {
        var input = "Calling `create_bulk_values` now.";
        AiTextCleaner.CleanForHistory(input).Should().BeEmpty();
    }

    [TestMethod]
    public void CleanForHistory_StripsLineWithToolNameAnywhere()
    {
        var input = "The update_coded_value was successful.";
        AiTextCleaner.CleanForHistory(input).Should().BeEmpty();
    }

    [TestMethod]
    public void CleanForHistory_StripsToolNarrationLine()
    {
        var input = "I'll use the tool to create a new value.";
        AiTextCleaner.CleanForHistory(input).Should().BeEmpty();
    }

    [TestMethod]
    public void CleanForHistory_StripsLetMeCallToolLine()
    {
        var input = "Let me call the function to get that data.";
        AiTextCleaner.CleanForHistory(input).Should().BeEmpty();
    }

    [TestMethod]
    public void CleanForHistory_StripsIWillInvokeLine()
    {
        var input = "I will invoke the API to update this value.";
        AiTextCleaner.CleanForHistory(input).Should().BeEmpty();
    }

    [TestMethod]
    public void CleanForHistory_PreservesLinesWithoutToolNames()
    {
        var input = "Created 3 values under COUNTRY-TYPE.\nAll done!";
        AiTextCleaner.CleanForHistory(input).Should().Be("Created 3 values under COUNTRY-TYPE.\nAll done!");
    }

    [TestMethod]
    public void CleanForHistory_MixedContent_StripsToolLines_KeepsProse()
    {
        var input = """
            I'll use create_bulk_values to add values.
            Created 5 country values under COUNTRY-TYPE.
            Now I will call the function again.
            All values are ready.
            """;
        var result = AiTextCleaner.CleanForHistory(input);
        result.Should().Contain("Created 5 country values under COUNTRY-TYPE.");
        result.Should().Contain("All values are ready.");
        result.Should().NotContain("create_bulk_values");
        result.Should().NotContain("call the function");
    }

    [TestMethod]
    public void CleanForHistory_AppliesDisplayCleaningFirst()
    {
        // History cleaning includes all display cleaning + extra
        var input = "Result: <thinking>hmm</thinking> create_coded_value was called.";
        var result = AiTextCleaner.CleanForHistory(input);
        result.Should().NotContain("<thinking>");
        result.Should().NotContain("create_coded_value");
    }

    [TestMethod]
    public void CleanForHistory_ReturnsEmptyForWhitespaceInput()
    {
        AiTextCleaner.CleanForHistory("   \n\t  ").Should().BeEmpty();
    }

    // =====================================================================
    // KEY REGRESSION TESTS — the bugs we're actually fixing
    // =====================================================================

    [TestMethod]
    public void CleanForDisplay_DoesNotStripProseThatMentionsToolName_InFinalSummary()
    {
        // REGRESSION: The old CleanModelText stripped this entirely because
        // the line contains "create_bulk_values". CleanForDisplay should NOT.
        var input = "I used create_bulk_values to create 5 country values under COUNTRY-TYPE.";
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("create 5 country values");
        result.Should().NotBeEmpty();
    }

    [TestMethod]
    public void CleanForHistory_StripsProseThatMentionsToolName()
    {
        // CleanForHistory SHOULD strip this — it's for history, not display
        var input = "I used create_bulk_values to create 5 country values under COUNTRY-TYPE.";
        var result = AiTextCleaner.CleanForHistory(input);
        result.Should().BeEmpty(); // the only line contains a tool name
    }

    [TestMethod]
    public void CleanForDisplay_StripsEmptyJsonTags_TheEmptyJsonRegression()
    {
        // REGRESSION: Model outputted {"code":"US"} which showed as empty tags
        var input = """Created: {"code": "US", "name": "United States"}""";
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Be("Created:");
        result.Should().NotContain("{}");
        result.Should().NotContain("\"code\"");
    }

    [TestMethod]
    public void CleanForDisplay_StripsDataJsonArray()
    {
        // Model sometimes outputs arrays of data objects
        var input = """Results: [{"id": "1", "code": "US"}, {"id": "2", "code": "CA"}] done.""";
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().NotContain("\"id\"");
        result.Should().Contain("done.");
    }

    [TestMethod]
    public void CleanForDisplay_FullScenario_ModelOutputAfterToolExecution()
    {
        // Realistic scenario: model returns a summary after executing tools,
        // mixed with some leaked syntax
        var input = """
            <thinking>I should report what I did</thinking>
            I've created 5 country values under the COUNTRY-TYPE category:
            - US: United States
            - CA: Canada
            - MX: Mexico
            - GB: United Kingdom
            - FR: France

            All values are now available in the system.
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("United States");
        result.Should().Contain("Canada");
        result.Should().Contain("All values are now available");
        result.Should().NotContain("<thinking>");
    }

    [TestMethod]
    public void CleanForDisplay_FullScenario_LeakedSyntaxMixedWithSummary()
    {
        // Model leaks some JSON data but also has human-readable text
        var input = """
            {"code": "US", "name": "United States", "id": "abc-123"}
            I've successfully created the United States value under COUNTRY-TYPE.
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("successfully created");
        result.Should().NotContain("\"code\"");
    }

    [TestMethod]
    public void CleanForDisplay_StripsFunctionDefJson_KeepsSurroundingProse()
    {
        var input = """
            Here's what I did:
            {"type": "function", "name": "create_coded_value", "parameters": {"code": "US"}}
            The value was created successfully.
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("The value was created successfully");
        result.Should().NotContain("\"type\"");
    }

    // =====================================================================
    // RemoveJsonBlocksContaining — direct tests
    // =====================================================================

    [TestMethod]
    public void RemoveJsonBlocksContaining_StripsMatchingBlock()
    {
        var input = """before {"type": "function", "name": "x"} after""";
        AiTextCleaner.RemoveJsonBlocksContaining(input, "\"type\"\\s*:\\s*\"function\"")
            .Should().Be("before  after");
    }

    [TestMethod]
    public void RemoveJsonBlocksContaining_PreservesNonMatchingBlock()
    {
        var input = """before {"theme": "dark"} after""";
        AiTextCleaner.RemoveJsonBlocksContaining(input, "\"type\"\\s*:\\s*\"function\"")
            .Should().Be("""before {"theme": "dark"} after""");
    }

    [TestMethod]
    public void RemoveJsonBlocksContaining_HandlesNestedBraces()
    {
        var input = """before {"function": {"name": "x", "params": {}}} after""";
        AiTextCleaner.RemoveJsonBlocksContaining(input, "\"function\"\\s*:\\s*\\{")
            .Should().Be("before  after");
    }

    [TestMethod]
    public void RemoveJsonBlocksContaining_HandlesMultilineJson()
    {
        var input = "before {\n  \"id\": \"1\",\n  \"code\": \"US\"\n} after";
        AiTextCleaner.RemoveJsonBlocksContaining(input, @"""(?:id|code)""\s*:")
            .Should().Be("before  after");
    }

    [TestMethod]
    public void RemoveJsonBlocksContaining_NoMatch_ReturnsOriginal()
    {
        var input = "no braces here";
        AiTextCleaner.RemoveJsonBlocksContaining(input, "\"type\"\\s*:\\s*\"function\"")
            .Should().Be("no braces here");
    }

    [TestMethod]
    public void RemoveJsonBlocksContaining_UnbalancedBrace_PreservesAsIs()
    {
        var input = """before {"type": "function" after""";
        // No closing brace — the block is not balanced, so it stays
        AiTextCleaner.RemoveJsonBlocksContaining(input, "\"type\"\\s*:\\s*\"function\"")
            .Should().Be("""before {"type": "function" after""");
    }

    [TestMethod]
    public void RemoveJsonBlocksContaining_EscapedQuotesInStrings()
    {
        var input = """before {"type": "function", "desc": "he said \"hello\""} after""";
        AiTextCleaner.RemoveJsonBlocksContaining(input, "\"type\"\\s*:\\s*\"function\"")
            .Should().Be("before  after");
    }

    // =====================================================================
    // Full scenario: "Add hospital coded values with parent code HSPTL"
    // Tests that the AI response produces a clean list of hospitals
    // =====================================================================

    [TestMethod]
    public void CleanForDisplay_HospitalBulkCreate_ResponseAsHospitalList()
    {
        // Model returns a clean bulleted list after tool execution — no leaking
        var input = """
            I've added the following hospital values under HSPTL:
            - GH: General Hospital
            - UH: University Hospital
            - CH: Children's Hospital
            - MH: Memorial Hospital

            All 4 values are now available.
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("General Hospital");
        result.Should().Contain("University Hospital");
        result.Should().Contain("Children's Hospital");
        result.Should().Contain("Memorial Hospital");
        result.Should().Contain("All 4 values are now available");
        result.Should().NotContain("<thinking>");
        result.Should().NotContain("\"code\"");
        result.Should().NotContain("\"id\"");
    }

    [TestMethod]
    public void CleanForDisplay_HospitalBulkCreate_ThinkingTagsStripped()
    {
        // Model leaks <thinking> before the list
        var input = """
            <thinking>The user wants hospitals under HSPTL. I'll use create_bulk_values.</thinking>
            I've added the following hospital values under HSPTL:
            - GH: General Hospital
            - UH: University Hospital
            - CH: Children's Hospital

            All 3 values are now available.
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("General Hospital");
        result.Should().Contain("University Hospital");
        result.Should().Contain("Children's Hospital");
        result.Should().NotContain("<thinking>");
        result.Should().NotContain("create_bulk_values");
    }

    [TestMethod]
    public void CleanForDisplay_HospitalBulkCreate_FunctionDefJsonStripped()
    {
        // Model leaks function-definition JSON before the readable list
        var input = """
            {"type": "function", "name": "create_bulk_values", "parameters": {"parentCode": "HSPTL", "items": [{"code": "GH", "name": "General Hospital"}]}}
            I've added the hospital values under HSPTL:
            - GH: General Hospital
            - UH: University Hospital
            - CH: Children's Hospital
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("General Hospital");
        result.Should().Contain("University Hospital");
        result.Should().Contain("Children's Hospital");
        result.Should().NotContain("\"type\"");
        result.Should().NotContain("\"function\"");
        result.Should().NotContain("create_bulk_values");
    }

    [TestMethod]
    public void CleanForDisplay_HospitalBulkCreate_JsonDataObjectsStripped()
    {
        // Model leaks data JSON objects alongside the human-readable list
        var input = """
            {"id": "h1", "code": "GH", "parentId": "HSPTL", "description": "General Hospital"}
            {"id": "h2", "code": "UH", "parentId": "HSPTL", "description": "University Hospital"}
            {"id": "h3", "code": "CH", "parentId": "HSPTL", "description": "Children's Hospital"}
            I've created 3 hospital values under HSPTL:
            - GH: General Hospital
            - UH: University Hospital
            - CH: Children's Hospital
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("General Hospital");
        result.Should().Contain("University Hospital");
        result.Should().Contain("Children's Hospital");
        result.Should().NotContain("\"id\"");
        result.Should().NotContain("\"parentId\"");
    }

    [TestMethod]
    public void CleanForDisplay_HospitalBulkCreate_ToolCallSyntaxStripped()
    {
        // Model leaks raw function-call-as-text syntax
        var input = """
            create_bulk_values(parentCode="HSPTL", items=[{"code":"GH","name":"General Hospital"},{"code":"UH","name":"University Hospital"}]);
            I've added 2 hospital values under HSPTL:
            - GH: General Hospital
            - UH: University Hospital
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("General Hospital");
        result.Should().Contain("University Hospital");
        result.Should().NotContain("create_bulk_values");
    }

    [TestMethod]
    public void CleanForDisplay_HospitalBulkCreate_StandaloneToolNameLineStripped()
    {
        // Model emits a standalone tool name line before the response
        var input = """
            → create_bulk_values
            Added hospital values under HSPTL:
            - GH: General Hospital
            - UH: University Hospital
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("General Hospital");
        result.Should().Contain("University Hospital");
        result.Should().NotContain("create_bulk_values");
    }

    [TestMethod]
    public void CleanForDisplay_HospitalBulkCreate_NameValueToolNameLineStripped()
    {
        // Model leaks "name": "tool_name" line
        var input = """
            "name": "create_bulk_values"
            Here are the hospital values I added under HSPTL:
            - GH: General Hospital
            - CH: Children's Hospital
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("General Hospital");
        result.Should().Contain("Children's Hospital");
        result.Should().NotContain("\"name\"");
        result.Should().NotContain("create_bulk_values");
    }

    [TestMethod]
    public void CleanForDisplay_HospitalBulkCreate_KitchenSinkAllLeaked()
    {
        // Worst case: model leaks thinking tags, function def, data objects,
        // tool name line, and empty JSON — but still has a valid readable list
        var input = """
            <thinking>User wants hospitals under HSPTL parent. I'll call create_bulk_values.</thinking>
            {"type": "function", "name": "create_bulk_values", "parameters": {"parentCode": "HSPTL"}}
            create_bulk_values
            {"id": "h1", "code": "GH", "parentId": "HSPTL", "description": "General Hospital"}
            {}
            I've added the following hospital values under HSPTL:
            - GH: General Hospital
            - UH: University Hospital
            - CH: Children's Hospital
            - MH: Memorial Hospital
            All 4 values are now available.
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("General Hospital");
        result.Should().Contain("University Hospital");
        result.Should().Contain("Children's Hospital");
        result.Should().Contain("Memorial Hospital");
        result.Should().Contain("All 4 values are now available");
        result.Should().NotContain("<thinking>");
        result.Should().NotContain("\"type\"");
        result.Should().NotContain("\"function\"");
        result.Should().NotContain("create_bulk_values");
        result.Should().NotContain("\"id\"");
        result.Should().NotContain("{}");
    }

    [TestMethod]
    public void CleanForHistory_HospitalBulkCreate_StripsToolNameFromProse()
    {
        // CleanForHistory is aggressive: even a line mentioning a tool name is removed
        var input = """
            I used create_bulk_values to add hospitals under HSPTL.
            - GH: General Hospital
            - UH: University Hospital
            All 2 values are now available.
            """;
        var result = AiTextCleaner.CleanForHistory(input);
        result.Should().NotContain("create_bulk_values");
        result.Should().Contain("General Hospital");
        result.Should().Contain("University Hospital");
        result.Should().Contain("All 2 values are now available");
    }

    [TestMethod]
    public void CleanForHistory_HospitalBulkCreate_StripsToolNarration()
    {
        // CleanForHistory strips "I'll use the tool..." narration
        var input = """
            I'll use the tool to create hospital values under HSPTL.
            - GH: General Hospital
            - CH: Children's Hospital
            """;
        var result = AiTextCleaner.CleanForHistory(input);
        result.Should().NotContain("I'll use the tool");
        result.Should().Contain("General Hospital");
        result.Should().Contain("Children's Hospital");
    }

    [TestMethod]
    public void CleanForDisplay_HospitalBulkCreate_EmptyJsonObjectTagsRemoved()
    {
        // Regression: model output includes {} which showed as empty tags
        var input = """
            {}
            Hospital values created under HSPTL:
            - GH: General Hospital
            - UH: University Hospital
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("General Hospital");
        result.Should().Contain("University Hospital");
        result.Should().NotContain("{}");
    }

    [TestMethod]
    public void CleanForDisplay_HospitalBulkCreate_ArrayOfJsonDataStripped()
    {
        // Model leaks a JSON array of coded-value objects
        var input = """Results: [{"id": "h1", "code": "GH", "description": "General Hospital"}, {"id": "h2", "code": "UH", "description": "University Hospital"}] Created 2 hospital values under HSPTL.""";
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("Created 2 hospital values under HSPTL");
        result.Should().NotContain("\"id\"");
        result.Should().NotContain("\"code\"");
    }

    [TestMethod]
    public void CleanForDisplay_HospitalBulkCreate_MultilineJsonDataBlockStripped()
    {
        // Model leaks multi-line JSON data block before the readable summary
        var input = """
            Created:
            {
              "id": "h1",
              "code": "GH",
              "parentId": "HSPTL",
              "description": "General Hospital"
            }
            Hospital values added under HSPTL:
            - GH: General Hospital
            - UH: University Hospital
            """;
        var result = AiTextCleaner.CleanForDisplay(input);
        result.Should().Contain("General Hospital");
        result.Should().Contain("University Hospital");
        result.Should().NotContain("\"id\"");
        result.Should().NotContain("\"parentId\"");
    }
}
