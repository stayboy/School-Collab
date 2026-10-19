using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.RichText;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// AC-2/AC-3/AC-4 (round <c>wysiwyg-instruction-editor</c>): the ONE sanitiser's allow-list, tested in
/// both directions — what the restricted toolbar can produce must survive, and everything else must be
/// refused. These are seams, which is what §8.4 Q17 asks for: the editor itself is JS-backed and stays
/// out of bUnit, so the guarantee is pinned here and at the render component.
/// </summary>
[TestClass]
public class RichInstructionSanitizerTests
{
    [TestMethod]
    public void KeepsWhatTheRestrictedToolbarCanProduce()
    {
        var html = "<h2>Steps</h2>"
            + "<p><strong>Read</strong> <em>pages</em> <u>4-6</u> <s>old</s></p>"
            + "<ol><li>One</li></ol><ul><li>Two</li></ul>"
            + "<p><a href=\"https://example.test/x\">Link</a></p>";

        var clean = RichInstructionSanitizer.Sanitize(html);

        clean.Should().Contain("<h2>Steps</h2>")
            .And.Contain("<strong>Read</strong>")
            .And.Contain("<em>pages</em>")
            .And.Contain("<u>4-6</u>")
            .And.Contain("<s>old</s>")
            .And.Contain("<ol>").And.Contain("<ul>").And.Contain("<li>One</li>")
            .And.Contain("href=\"https://example.test/x\"");
    }

    [TestMethod]
    public void DropsAScriptAndItsContent()
    {
        var clean = RichInstructionSanitizer.Sanitize(
            "<p>Before</p><script>alert('x')</script><p>After</p>");

        clean.Should().NotContain("script")
            .And.NotContain("alert", "the payload goes, not merely the tag around it")
            .And.Contain("Before").And.Contain("After");
    }

    [TestMethod]
    public void DropsEventHandlersInlineStylesAndUnlistedElements()
    {
        var clean = RichInstructionSanitizer.Sanitize(
            "<p onclick=\"steal()\" style=\"color:red\">Text</p>"
            + "<img src=\"https://example.test/i.png\">"
            + "<iframe src=\"https://example.test\"></iframe>");

        clean.Should().NotContain("onclick")
            .And.NotContain("style=", "the allow-list is tags-only, so no inline style can arrive")
            .And.NotContain("<img", "Q14: media is an instruction ITEM, never an inline embed")
            .And.NotContain("<iframe")
            .And.Contain("Text", "the prose itself is not the problem");
    }

    [TestMethod]
    public void DropsADangerousLinkTarget()
    {
        var clean = RichInstructionSanitizer.Sanitize("<a href=\"javascript:steal()\">Click</a>");

        clean.Should().NotContain("javascript:").And.Contain("Click");
    }

    [TestMethod]
    public void PlainTextIsUntouched()
    {
        RichInstructionSanitizer.Sanitize("Read pages 4-6.").Should().Be("Read pages 4-6.");

        // A bare comparison is not markup: the text SURVIVES, entity-encoded. The rendered result is
        // character-for-character what the author typed, which is the property Q16 is about — so this
        // asserts the encoding rather than pretending nothing happens to the string.
        RichInstructionSanitizer.Sanitize("5 < 6 and 7 > 6").Should().Be("5 &lt; 6 and 7 &gt; 6");
    }

    [TestMethod]
    public void NullOrWhitespaceBecomesEmpty()
    {
        RichInstructionSanitizer.Sanitize(null).Should().BeEmpty();
        RichInstructionSanitizer.Sanitize("   ").Should().BeEmpty();
    }

    /// <summary>The display-side plain-text projection (grill extension, owner 2026-10-11): a one-line
    /// readout shows the WORDS, not the markup, and Quill's empty payload counts as empty — which is
    /// what the material dialog then stores as a null body.</summary>
    [TestMethod]
    public void ToPlainText_DropsTagsDecodesEntities_AndNamesTheEmptyPayloadEmpty()
    {
        RichInstructionSanitizer.ToPlainText("<p>Read <strong>pages 4-6</strong>.</p>")
            .Should().Be("Read pages 4-6.");
        RichInstructionSanitizer.ToPlainText("A &amp; B").Should().Be("A & B");
        RichInstructionSanitizer.ToPlainText("plain text").Should().Be("plain text");
        RichInstructionSanitizer.ToPlainText("<p><br></p>").Should().BeEmpty("Quill's empty payload is empty");
        RichInstructionSanitizer.ToPlainText(null).Should().BeEmpty();
    }
}
