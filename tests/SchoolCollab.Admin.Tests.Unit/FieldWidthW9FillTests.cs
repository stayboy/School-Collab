using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// <b>The ladder's "fill" step actually fills</b> (#414, owner round debt-sweep 2026-10-27).
///
/// <para><c>FieldWidth.W9.ToCssStyle</c> emitted <c>flex: 1 1 0</c> while <c>.w-9</c> in
/// <c>src/SchoolCollab.Admin/wwwroot/css/app.css</c> — the same ladder step expressed as a class —
/// used <c>flex: 1 1 auto</c>. The difference is not cosmetic. The ladder's inline style lands on an
/// element that is a <b>flex item</b> (the dropdown wrappers' <c>fluent-field</c> host; a
/// <c>FormRow</c> input cell is <c>display:flex</c> per <c>FormRow.razor.css</c>), and with a zero
/// flex-basis the used width is resolved by the flex algorithm from the item's content instead of
/// from the <c>width:100%</c> beside it — so a W9 field sized to its own text and read as the same
/// width as a fixed-width sibling. An <c>auto</c> basis makes the <c>100%</c> the starting size, so
/// the field fills the cell before it grows.</para>
///
/// <para>Two assertions, because the defect was the two expressions of one ladder step disagreeing:
/// the emitted inline style, and its agreement with the <c>.w-9</c> class. Both fail if someone
/// "tidies" the basis back to <c>0</c>.</para>
/// </summary>
[TestClass]
public class FieldWidthW9FillTests
{
    /// <summary>The App host's global ladder sheet — the class half of the same step.</summary>
    private static string ReadAppCss()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var path = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "SchoolCollab.Admin", "wwwroot", "css", "app.css"));
        File.Exists(path).Should().BeTrue($"app.css should exist at '{path}'");
        return File.ReadAllText(path);
    }

    /// <summary>The declaration block of the <c>.w-9</c> rule (the ladder's fill step as a class),
    /// with runs of whitespace collapsed so a reformat of the sheet does not matter.</summary>
    private static string W9ClassRule()
    {
        var match = Regex.Match(ReadAppCss(), @"\.w-9\s*\{(?<decls>[^}]*)\}");
        match.Success.Should().BeTrue("app.css must define the .w-9 fill step");
        return Regex.Replace(match.Groups["decls"].Value, @"\s+", " ").Trim();
    }

    /// <summary>
    /// W9 keeps an <c>auto</c> flex-basis. A zero basis resolves the width from the flex algorithm,
    /// not from the 100%, and the field stops filling its cell.
    /// </summary>
    [TestMethod]
    public void W9_EmitsAnAutoFlexBasis()
    {
        var style = FieldWidth.W9.ToCssStyle();

        style.Should().NotContain("flex:1 1 0",
            "a zero flex-basis resolves the width from the flex algorithm instead of the 100%, which is "
            + "the #414 'W9 does not fill its cell' defect");
        style.Should().Contain("flex:1 1 auto",
            "the auto basis is what makes the 100% the starting size in a flex parent (#414)");
        style.Should().Contain("width:100%",
            "and the 100% alone must still fill a non-flex parent");
    }

    /// <summary>
    /// The inline style and the <c>.w-9</c> class are the same ladder step and must not drift: the
    /// class was already fixed, the C# helper was not — which is how #414 shipped.
    /// </summary>
    [TestMethod]
    public void W9_AgreesWith_TheW9CssClass()
    {
        var style = FieldWidth.W9.ToCssStyle();
        var rule = W9ClassRule();

        style.Should().Contain("flex:1 1 auto");
        rule.Should().Contain("flex: 1 1 auto",
            "app.css's .w-9 carries the same fix and is the documented home of the reason");
        style.Should().Contain("width:100%");
        rule.Should().Contain("width: 100%");
    }
}
