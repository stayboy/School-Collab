using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the <c>DialogShellFooter</c> parameter-binding trap found on 2026-10-01 (round
/// <c>assignment-policy-ui</c>, reported post-close from the policy edit dialog by the owner).
///
/// <para><c>Error</c> is a <c>string?</c> parameter and the footer renders a danger message bar
/// whenever it is non-empty. On a <b>string</b> parameter a bare attribute value is a <b>string
/// literal</b>, so <c>Error="Error"</c> passes the text <c>"Error"</c> — painting a permanent red
/// bar reading "Error" on every open, whether or not anything failed. The <c>Saving="Saving"</c>
/// written right beside it looks identical but is a <c>bool</c>, so its bare token binds the base
/// property and works; that contrast is what camouflaged the mistake in four dialogs and in the
/// <c>dialog-ui</c> skill's own example.</para>
///
/// <para>This is the <b>class-level</b> guard: the defect was found and fixed one dialog at a time
/// (AssignmentPolicy, ActivityGroupCreate, ActivityGroupEdit, NotificationPolicyFieldEdit), so
/// instead of a per-dialog bUnit assertion this scans every <c>.razor</c> file and fails on any
/// literal passed to the footer's <c>Error</c>. It also flags the bare <c>Saving="Saving"</c> form,
/// which binds correctly but is precisely what keeps the trap plausible.</para>
/// </summary>
[TestClass]
public class DialogShellFooterBindingArchitectureTests
{
    private static readonly string SrcRoot = FindSrcRoot();

    /// <summary>A bare value on the <c>string?</c> <c>Error</c> parameter is a literal. Correct:
    /// <c>Error="@Error"</c>.</summary>
    private static readonly Regex LiteralErrorArgument =
        new("""\bError="(?!@)""", RegexOptions.Compiled);

    /// <summary><c>Saving</c> is a <c>bool</c>, so the bare form works — and hides the bug next to it.</summary>
    private static readonly Regex BareSavingArgument =
        new("""\bSaving="(?!@)""", RegexOptions.Compiled);

    /// <summary><c>@* … *@</c> razor comments — a commented-out example must not fail the guard.</summary>
    private static readonly Regex RazorComment =
        new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex FooterTag =
        new(@"<DialogShellFooter\b", RegexOptions.Compiled);

    [TestMethod]
    public void DialogShellFooter_NeverReceivesALiteralParameterValue()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var text = RazorComment.Replace(File.ReadAllText(file), string.Empty);

            foreach (Match tag in FooterTag.Matches(text))
            {
                var element = ElementText(text, tag.Index);

                if (LiteralErrorArgument.IsMatch(element))
                {
                    offenders.Add(
                        $"{Relative(file)} — Error=\"…\" is a string LITERAL (a bare value on a string " +
                        "parameter). Use Error=\"@Error\", otherwise the footer paints a permanent " +
                        "\"Error\" danger bar on every open.");
                }

                if (BareSavingArgument.IsMatch(element))
                {
                    offenders.Add(
                        $"{Relative(file)} — use Saving=\"@Saving\". The bare form binds correctly " +
                        "(Saving is a bool), but it is what makes Error=\"Error\" look right.");
                }
            }
        }

        offenders.Should().BeEmpty(
            "DialogShellFooter parameters must be BOUND (@Error / @Saving), never passed as literals");
    }

    /// <summary>The element's own text — from the tag through its terminating <c>/&gt;</c> (or
    /// <c>&gt;</c>), capped so an unterminated tag cannot swallow the file.</summary>
    private static string ElementText(string text, int startIndex)
    {
        const int maxLength = 400;
        var tail = text.Substring(startIndex, Math.Min(maxLength, text.Length - startIndex));
        var selfClosing = tail.IndexOf("/>", StringComparison.Ordinal);
        var openEnd = tail.IndexOf('>');
        var end = selfClosing >= 0 && (openEnd < 0 || selfClosing < openEnd) ? selfClosing + 2
            : openEnd >= 0 ? openEnd + 1
            : tail.Length;
        return tail[..end];
    }

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.razor", SearchOption.AllDirectories)
            .Where(p => !p.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                     && !p.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase));

    private static string Relative(string path) => path.Replace(SrcRoot, "src");

    private static string FindSrcRoot()
    {
        var asmDir = Path.GetDirectoryName(typeof(DialogShellFooterBindingArchitectureTests).Assembly.Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "..", "src"));
    }
}
