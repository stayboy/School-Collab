using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// <b>A dropdown is never sized below <c>FieldWidth.W3</c> (160px)</b> — the repo's W1–W9 ladder
/// (see <c>SchoolCollab.Admin.Shared/Components/FieldWidth.cs</c>) sizes a FluentUI input from the
/// <c>fluent-field</c> host, but Fluent v5 hard-codes <c>min-width: 160px</c> on the
/// <c>fluent-dropdown</c> shadow <c>.control</c> and the host keeps <c>overflow: visible</c>. A narrow
/// host therefore does not shrink the control: the control PAINTS PAST it and covers the neighbouring
/// field — the defect measured on 2026-10-24 in the student edit form's add-contact row, where the
/// <c>ContactChannel</c> dropdown at W2 (120px) painted 160px over the "Label (optional)" input, and
/// hit-testing at that input's left edge returned <c>fluent-dropdown</c>.
///
/// <para>The floor cannot be expressed in CSS — the offending <c>min-width</c> lives inside the web
/// component's shadow root, so no outer rule can reach it (the alternative, clipping the host with
/// <c>overflow: hidden</c>, was rejected: it would hide the next too-narrow dropdown instead of failing
/// loudly, and can clip the focus ring). It is a review rule, and this scan is what enforces it.</para>
///
/// <para>Following the <see cref="RazorMarkupHygieneArchitectureTests"/> convention, the rule was
/// verified to hold across the tree before it was enabled: after the three <c>ContactChannel</c> sites
/// moved to W3, no <c>.razor</c> file in <c>src/</c> sizes a dropdown wrapper at W1 or W2, and neither
/// step has a call site at all. The second scan covers the case the first cannot see — a <b>raw</b>
/// <c>FluentSelect</c> taking a literal width, which is how the guardians relationship picker shipped
/// at 150px (fixed the same day, owner Q8).</para>
/// </summary>
[TestClass]
public class DropdownWidthFloorArchitectureTests
{
    private static readonly string SrcRoot = FindSrcRoot();

    /// <summary>The wrappers that render a <c>fluent-dropdown</c> (their <c>Width</c> parameter emits the
    /// ladder's inline style onto the <c>fluent-field</c> host).</summary>
    private static readonly string[] DropdownWrappers =
        ["<DropdownForEnum", "<CodedValueDropdown", "<DropdownComponent"];

    /// <summary>Controls that ARE a dropdown — a raw <c>FluentSelect</c> / <c>FluentDropdown</c> / <c>FluentAutocomplete</c>
    /// bypasses the ladder entirely and can carry a literal width (that is how the guardians relationship picker
    /// shipped at 150px), so they are scanned for a sub-floor literal as well.</summary>
    private static readonly string[] RawDropdownControls =
        ["<FluentSelect", "<FluentDropdown", "<FluentAutocomplete"];

    /// <summary>The floor: <c>FieldWidth.W3</c> = 160px, identical to Fluent v5's own shadow-root minimum.</summary>
    private const int FloorPx = 160;

    private static string FindSrcRoot()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "..", "src"));
    }

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.razor", SearchOption.AllDirectories)
            .Where(p => !p.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                        && !p.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase));

    private static string Relative(string path) => path.Replace(SrcRoot, "src");

    /// <summary>Strips <c>@* … *@</c> comments, so a commented-out width is not a violation.</summary>
    private static string StripRazorComments(string text) =>
        Regex.Replace(text, "@\\*.*?\\*@", string.Empty, RegexOptions.Singleline);

    /// <summary>
    /// Yields each opening tag that starts with <paramref name="tagName"/>, up to and including its
    /// terminating <c>&gt;</c>. A manual scan (not a <c>[^&gt;]*</c> regex) because attribute values
    /// legitimately contain <c>&gt;</c> — e.g. <c>OnClick="@(() =&gt; …)"</c>.
    /// </summary>
    private static IEnumerable<string> OpeningTags(string text, string tagName)
    {
        var index = 0;
        while ((index = text.IndexOf(tagName, index, StringComparison.Ordinal)) >= 0)
        {
            var quote = '\0';
            var end = -1;
            for (var i = index + tagName.Length; i < text.Length; i++)
            {
                var ch = text[i];
                if (quote != '\0')
                {
                    if (ch == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (ch is '"' or '\'')
                {
                    quote = ch;
                    continue;
                }

                if (ch == '>')
                {
                    end = i;
                    break;
                }
            }

            if (end < 0)
            {
                yield break;
            }

            yield return text[index..(end + 1)];
            index = end + 1;
        }
    }

    private static string FirstLine(string tag)
    {
        var newline = tag.IndexOf('\n');
        return (newline < 0 ? tag : tag[..newline]).Trim();
    }

    [TestMethod]
    public void NoDropdown_SizedBelow_FieldWidth_W3()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var text = StripRazorComments(File.ReadAllText(file));

            foreach (var wrapper in DropdownWrappers)
            {
                foreach (var tag in OpeningTags(text, wrapper))
                {
                    if (tag.Contains("FieldWidth.W1", StringComparison.Ordinal)
                        || tag.Contains("FieldWidth.W2", StringComparison.Ordinal))
                    {
                        offenders.Add($"{Relative(file)}: {FirstLine(tag)}");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "a dropdown sized below FieldWidth.W3 (160px) paints past its host and covers the "
            + "neighbouring field, because Fluent v5 hard-codes min-width:160px on the "
            + "fluent-dropdown shadow .control while the host keeps overflow:visible "
            + "(measured 2026-10-24 in the student edit form's add-contact row)");
    }

    /// <summary>
    /// The same floor for a <b>raw</b> dropdown control, which the wrapper scan above cannot see: a
    /// <c>FluentSelect</c> / <c>FluentDropdown</c> / <c>FluentAutocomplete</c> takes a literal
    /// <c>Style="width: …"</c> or a <c>w-1</c>/<c>w-2</c> class, both of which land directly on the host.
    /// The guardians relationship picker shipped <c>Style="width: 150px"</c> this way and its 160px control
    /// painted 2px over the adjacent Last-name input (measured 2026-10-24, owner Q8).
    /// </summary>
    [TestMethod]
    public void NoRawDropdownControl_SizedBelow_FieldWidth_W3()
    {
        var offenders = new List<string>();
        var literal = new Regex(@"width\s*:\s*(?<px>\d+)\s*px", RegexOptions.IgnoreCase);
        var subFloorClass = new Regex(@"\bw-[12]\b");

        foreach (var file in RazorFiles())
        {
            var text = StripRazorComments(File.ReadAllText(file));

            foreach (var control in RawDropdownControls)
            {
                foreach (var tag in OpeningTags(text, control))
                {
                    var tooNarrowLiteral = literal.Matches(tag)
                        .Any(m => int.TryParse(m.Groups["px"].Value, out var px) && px < FloorPx);

                    if (tooNarrowLiteral || subFloorClass.IsMatch(tag))
                    {
                        offenders.Add($"{Relative(file)}: {FirstLine(tag)}");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "a raw dropdown control sized below 160px carries the same paint-over defect as the "
            + "wrapper half, and the ladder (FieldWidth.W3, or w-3) is the only sanctioned width "
            + "(owner Q8, measured 2026-10-24 in the guardians relationship picker at 150px)");
    }
}
