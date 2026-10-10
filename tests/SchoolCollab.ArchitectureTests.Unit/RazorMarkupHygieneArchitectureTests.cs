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
/// Razor <b>markup hygiene</b> — the source-shape guards for the two defect classes that no compiler,
/// linter or review reliably catches, because both produce a file that <b>compiles cleanly</b>:
///
/// <list type="number">
/// <item><b>A block keyword severed from its body.</b> <c>@if (cond)</c> on one line with its <c>{ … }</c>
/// far below (an edit inserted another block between them) still compiles: Razor emits the braces as
/// literal markup and the guarded element renders <em>unconditionally</em>. Found in
/// <c>InstructionEditorList.razor</c> as a stray "}" on the Create page (2026-10-09) — the compiler
/// reported nothing, and the component's own tests asserted only the ids that DO exist, so nothing
/// failed.</item>
/// <item><b>An inline <c>&lt;style&gt;</c> in a component.</b> A component's rules belong in its
/// <c>.razor.css</c>; an always-rendered <c>&lt;style&gt;</c> leaks them to the whole document and
/// re-emits them on every render (three files carried this until the same change moved them to scoped
/// CSS).</item>
/// </list>
///
/// <para>The third rule is the <c>MarkupString</c> ban (question-response-types §8 Q15: rich text goes
/// through the shared sanitiser + render component). It is a source scan rather than a banned-API
/// analyzer because this repo wires no analyzers at all and documents
/// <c>SchoolCollab.ArchitectureTests.Unit</c> as its enforcement vehicle for checkable anti-patterns
/// (see <c>Directory.Build.props</c>).</para>
///
/// <para>Following the <see cref="DotNetBestPracticesArchitectureTests"/> convention, every rule here was
/// verified to hold across the current tree before it was enabled — the block-keyword scan reports zero
/// findings, and the inline-style rule landed only after the three offending files were migrated.</para>
/// </summary>
[TestClass]
public class RazorMarkupHygieneArchitectureTests
{
    private static readonly string SrcRoot = FindSrcRoot();

    /// <summary>The markup-level block keywords: each one opens a body that must follow immediately,
    /// otherwise the body is orphaned markup rather than the block's body.</summary>
    private static readonly string[] BlockKeywords =
        ["@if", "@else", "@else if", "@foreach", "@for", "@while", "@switch", "@try", "@lock"];

    /// <summary>The one legitimate script in a razor file: the Blazor host document booting the
    /// framework (Admin's and Families' <c>App.razor</c>). That is the HTML shell, not a component.</summary>
    private const string FrameworkBootstrap = "_framework/blazor.web.js";

    private static string FindSrcRoot()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "..", "src"));
    }

    private static IEnumerable<string> SourceFiles(string[] extensions) =>
        Directory.EnumerateFiles(SrcRoot, "*", SearchOption.AllDirectories)
            .Where(p => extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .Where(p => !p.Contains("\\obj\\") && !p.Contains("\\bin\\"));

    private static IEnumerable<string> RazorFiles() => SourceFiles([".razor"]);

    private static string Relative(string path) => path.Replace(SrcRoot, "src");

    /// <summary>Strips <c>@* … *@</c> comments, so a commented-out rule is not a violation.</summary>
    private static string StripRazorComments(string text) =>
        Regex.Replace(text, "@\\*.*?\\*@", string.Empty, RegexOptions.Singleline);

    /// <summary>
    /// A markup-level block keyword must be followed by its opening brace — on the same line, or on the
    /// next non-blank line. A guard separated from its body compiles, but its braces arrive in the
    /// template as text and the element it guards always renders.
    /// </summary>
    [TestMethod]
    public void NoSeveredBlockGuards_InRazor()
    {
        var failures = new List<string>();

        foreach (var file in RazorFiles())
        {
            var lines = File.ReadAllLines(file);

            // A component's own C# lives in `@code`/`@functions`; only MARKUP-level keywords are
            // checked, because markup is the only place a guard and its body can drift apart.
            var inCode = false;
            var depth = 0;

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();

                if (inCode)
                {
                    depth += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
                    if (depth <= 0)
                    {
                        inCode = false;
                    }

                    continue;
                }

                if (trimmed.StartsWith("@code", StringComparison.Ordinal)
                    || trimmed.StartsWith("@functions", StringComparison.Ordinal))
                {
                    inCode = true;
                    depth = Math.Max(1, lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}'));
                    continue;
                }

                var keyword = BlockKeywords.FirstOrDefault(k => trimmed.StartsWith(k, StringComparison.Ordinal));
                if (keyword is null || trimmed.Contains('{'))
                {
                    continue;
                }

                var next = i + 1;
                while (next < lines.Length && lines[next].Trim().Length == 0)
                {
                    next++;
                }

                var following = next < lines.Length ? lines[next].Trim() : "(end of file)";

                // The brace must follow, full stop. An exemption for a following `@…` line is exactly
                // the hole that let the original defect through: there, the next line was `@code {` —
                // an `@` — so a "@ is fine" carve-out excused it. Razor requires braces for every one of
                // these blocks, so there is no legitimate `@`-starting successor.
                if (!following.StartsWith("{", StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{Relative(file)}:{i + 1} — '{keyword}' is not followed by '{{' (next non-blank " +
                        $"line: '{following}'). A guard separated from its body compiles, but the braces " +
                        "then render as literal text and the element it guards always renders.");
                }
            }

            // Blindness check (the mirror of the defect this rule exists for): if the tracker never
            // leaves the code block, its brace count desynced — a brace inside a string literal or a
            // comment — and the rule stopped looking at the rest of the file without saying so. Fail
            // loudly instead: a guard that quietly stops looking is worse than no guard.
            if (inCode)
            {
                failures.Add(
                    $"{Relative(file)} never leaves its @code/@functions block — the brace tracker " +
                    "desynced (most likely a brace inside a string literal or comment), which would " +
                    "silently stop this rule from looking at the rest of the file.");
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// No component may carry an inline <c>&lt;style&gt;</c> (its rules belong in its
    /// <c>.razor.css</c>) and no razor file may carry a <c>&lt;script&gt;</c> other than the host
    /// document's framework bootstrap.
    /// </summary>
    [TestMethod]
    public void NoInlineStyleOrScriptElements_InRazor()
    {
        var failures = new List<string>();

        foreach (var file in RazorFiles())
        {
            var text = StripRazorComments(File.ReadAllText(file));

            if (text.Contains("<style", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"{Relative(file)} contains an inline <style> element — a component's rules belong in " +
                    "its own .razor.css (CSS isolation). An always-rendered <style> leaks globally and " +
                    "re-emits on every render.");
            }

            foreach (Match script in Regex.Matches(text, "<script[^>]*>", RegexOptions.IgnoreCase))
            {
                if (!script.Value.Contains(FrameworkBootstrap, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{Relative(file)} contains a <script> element ({script.Value.Trim()}) — components " +
                        "must not inject scripts; a collocated JS module loaded through IJSObjectReference is " +
                        "the repo's interop seam.");
                }
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// <c>MarkupString</c> is a never (question-response-types §8 Q15): rich text is sanitised on write
    /// and again on render by ONE shared render component + sanitiser helper, never injected ad hoc.
    /// </summary>
    [TestMethod]
    public void NoMarkupString_InSource()
    {
        var failures = SourceFiles([".cs", ".razor"])
            .Where(f => File.ReadAllText(f).Contains("MarkupString", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        failures.Should().BeEmpty(
            "MarkupString is a never — rich/HTML content must go through the shared sanitiser + render " +
            "component (question-response-types §8 Q15). Files:\n" + string.Join("\n", failures));
    }
}
