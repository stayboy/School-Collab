using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.RichText;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// <see cref="PlaceholderTagMenu"/>'s token contract: an item's label IS the text the editor inserts, and
/// only the strict shape that label produces is ever admitted back into the message body.
/// </summary>
[TestClass]
public class PlaceholderTagMenuTests
{
    [TestMethod]
    public void Token_WrapsTheBareTagInTheTemplatesOwnBraces()
    {
        var tokens = NotificationTemplateTags.All.Select(PlaceholderTagMenu.Token).ToList();

        tokens.Should().OnlyContain(t =>
            t.StartsWith("{{", StringComparison.Ordinal) && t.EndsWith("}}", StringComparison.Ordinal));
        tokens.Should().HaveCount(NotificationTemplateTags.All.Count);
    }

    [TestMethod]
    [DataRow("{{Title}}", "{{Title}}")]
    [DataRow("  {{LinkExpiry}}  ", "{{LinkExpiry}}", DisplayName = "surrounding whitespace is trimmed")]
    [DataRow("{{A1}}", "{{A1}}", DisplayName = "digits are part of the name")]
    public void AMenuItemTextInTheTokenShape_IsTheToken(string text, string expected) =>
        PlaceholderTagMenu.TokenFromText(text).Should().Be(expected);

    [TestMethod]
    [DataRow(null, DisplayName = "no text")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("Assignment", DisplayName = "a GROUP parent's own label is prose, not a token")]
    [DataRow("Assignment{{Title}}{{Description}}", DisplayName = "a parent carrying its whole sub-menu")]
    [DataRow("{{}}", DisplayName = "no name")]
    [DataRow("{{Not A Tag}}", DisplayName = "a space is not a name")]
    [DataRow("{{Ti-tle}}", DisplayName = "a hyphen is not a name")]
    [DataRow("x{{Title}}", DisplayName = "text before the token")]
    [DataRow("{{Title}}x", DisplayName = "text after the token")]
    public void AnythingElse_IsRefused(string? text) =>
        PlaceholderTagMenu.TokenFromText(text).Should().BeNull();
}
