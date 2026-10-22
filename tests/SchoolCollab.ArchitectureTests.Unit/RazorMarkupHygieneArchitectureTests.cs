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
///
/// <para>Two CSS rules join them, for the same reason — both classes compile cleanly and fail
/// <b>silently</b>, and one of them caused a visible post-merge defect during the FluentUI v5 upgrade
/// (2026-10-17):</para>
/// <list type="number">
/// <item><b>A stray <c>*/</c>.</b> A <c>*/</c> written INSIDE comment text closes the comment early; the
/// leftover text is parsed as CSS and can swallow the rule that follows it. In
/// <c>ActiveTermToolbar.razor.css</c> a comment spelling <c>--neutral-*</c>-style names ended the comment
/// mid-sentence, the base <c>.active-term-link</c> rule was consumed as garbage, its <c>--term-fg</c>
/// never reached the DOM, and the component's <c>var(--term-fg)</c> declarations became invalid — the
/// toolbar's period text rendered blue-on-blue and invisible.</item>
/// <item><b>A legacy design token with no fallback.</b> v5 defines no <c>--neutral-*</c> or
/// <c>--accent-*</c> custom property at all (its design tokens are <c>--color*</c>), so
/// <c>var(--neutral-stroke-rest)</c> is unresolvable, which makes its <b>whole declaration</b> invalid at
/// computed-value time and silently dropped — how the folder-tab borders disappeared.</item>
/// </list>
/// </summary>
[TestClass]
public class RazorMarkupHygieneArchitectureTests
{
    private static readonly string SrcRoot = FindSrcRoot();

    /// <summary>The markup-level block keywords: each one opens a body that must follow immediately,
    /// otherwise the body is orphaned markup rather than the block's body.</summary>
    private static readonly string[] BlockKeywords =
        ["@if", "@else", "@else if", "@foreach", "@for", "@while", "@switch", "@try", "@lock"];

    /// <summary>A <c>[Parameter]</c> declaration whose property is a <c>string</c> (the only type on
    /// which a quoted attribute value is a LITERAL rather than an expression).</summary>
    private const string StringParameterPattern = @"\[Parameter[^\]]*\]\s*public\s+string\??\s+(\w+)";

    /// <summary>A component tag with its attribute text; quoted values may contain <c>&gt;</c>.</summary>
    private const string ComponentTagPattern = "<([A-Z]\\w+)((?:[^>\"\\n]|\"[^\"]*\")*)>";

    /// <summary>An attribute whose quoted value is a bare <c>_</c>-prefixed member access.</summary>
    private const string BareFieldLiteralPattern = "(?<![\\w@-])(\\w+)\\s*=\\s*\"(_+[A-Za-z0-9_]*\\.[A-Za-z0-9_.]+)\"";

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
    ///
    /// <para>TWO files are allowed, by name, each for a reason the round doc records (G3):
    /// <c>RichInstruction.razor</c> <b>is</b> the shared render component — the single place a sanitised
    /// value becomes markup, which is where this rule is enforced rather than broken; and
    /// <c>RichInstructionEditor.razor</c> hands the editor wrapper its TOOLBAR, which is our own constant
    /// markup and never user input. Everything else stays banned: do not extend this list to admit a
    /// convenience — that is exactly what the ban exists to prevent.</para>
    /// </summary>
    [TestMethod]
    public void NoMarkupString_InSource()
    {
        // Path fragments, normalised to '/' so the check holds whichever separator a caller reports.
        string[] allowed =
        [
            "Components/RichText/RichInstruction.razor",
            "Components/RichText/RichInstructionEditor.razor",
        ];

        var failures = SourceFiles([".cs", ".razor"])
            .Where(f => File.ReadAllText(f).Contains("MarkupString", StringComparison.Ordinal))
            .Select(Relative)
            .Where(rel => !allowed.Any(a => rel.Replace('\\', '/').EndsWith(a, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        failures.Should().BeEmpty(
            "MarkupString is a never — rich/HTML content must go through the shared sanitiser + render " +
            "component (question-response-types §8 Q15), except the two files named in this test. Files:\n" +
            string.Join("\n", failures));
    }

    /// <summary>
    /// A <c>*/</c> outside a comment closes an EARLIER comment than the author intended. Everything the
    /// author meant to keep commented out becomes CSS: the parser can consume the following rule as
    /// garbage, so a rule vanishes without a build error, a warning or a failing test.
    ///
    /// <para>Found in <c>ActiveTermToolbar.razor.css</c> (2026-10-17): a comment naming legacy tokens
    /// contained <c>--neutral-*/--accent-*</c>, whose <c>*/</c> ended the comment early and swallowed the
    /// base <c>.active-term-link</c> rule — the toolbar's period text then fell back to the link's own
    /// accent colour and rendered blue on the blue header.</para>
    /// </summary>
    [TestMethod]
    public void NoStrayCommentTerminator_InCss()
    {
        var failures = new List<string>();

        foreach (var file in SourceFiles([".css"]))
        {
            var text = File.ReadAllText(file);
            var inComment = false;

            for (var i = 0; i < text.Length; i++)
            {
                var pair = i + 1 < text.Length ? text.Substring(i, 2) : null;

                if (pair == "/*")
                {
                    inComment = true;
                    i++;
                    continue;
                }

                if (pair != "*/")
                {
                    continue;
                }

                if (!inComment)
                {
                    var line = text.Take(i).Count(c => c == '\n') + 1;
                    failures.Add(
                        $"{Relative(file)}:{line} — a '*/' appears OUTSIDE a comment. It closes an earlier " +
                        "comment than intended, and the leftover text is parsed as CSS — which can consume " +
                        "the next rule whole, silently. Rewrite the comment so it contains no '*/'.");
                    break;
                }

                inComment = false;
                i++;
            }

            if (inComment)
            {
                failures.Add($"{Relative(file)} — a '/*' is never closed.");
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The files whose v4 design tokens silently died in v5 must reference the v5 vocabulary
    /// (<c>--color*</c>) and carry no bare legacy token. Each of these two files caused a visible
    /// post-merge regression when its token stopped resolving: the toolbar's period text became
    /// blue-on-blue and the folder-tab borders vanished.
    ///
    /// <para>A fallback would also make a legacy name harmless (<c>var(--neutral-x, #d1d1d1)</c>), but
    /// these two are repointed rather than patched, so the intent is visible: use the token the design
    /// system actually defines. The rest of the tree still carries legacy usages (102 without a
    /// fallback, tracked in <c>documents/solution/fluentui-v5-residual-backlog.md</c>); this rule pins the
    /// two regressions that were actually observed and must not creep back.</para>
    /// </summary>
    [TestMethod]
    public void RepointedFiles_UseTheV5ColourTokens_WithNoBareLegacyToken()
    {
        // Path fragments, normalised to '/', so the check holds whichever separator a caller reports.
        string[] repointed =
        [
            "Students.Application/Components/Toolbar/ActiveTermToolbar.razor.css",
            "Admin.Shared/Components/FluentTabStrip.razor.css",
        ];

        var failures = new List<string>();

        foreach (var file in SourceFiles([".css"]))
        {
            var rel = Relative(file).Replace('\\', '/');
            var match = repointed.FirstOrDefault(r => rel.EndsWith(r, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                continue;
            }

            var text = File.ReadAllText(file);

            // The declared value, not the fallback: a bare `var(--neutral-…)` is the defect.
            foreach (Match token in Regex.Matches(text, @"var\(\s*--(?:neutral|accent|foreground-on)[a-z0-9-]*\s*\)"))
            {
                var line = text.Take(token.Index).Count(c => c == '\n') + 1;
                failures.Add(
                    $"{rel}:{line} — '{token.Value}' names a v4 token that v5 does not define, so the " +
                    "whole declaration is dropped silently. Use the v5 token (--color*) or give the " +
                    "legacy name a literal fallback.");
            }

            if (!text.Contains("--color", StringComparison.Ordinal))
            {
                failures.Add($"{rel} — expected at least one v5 '--color*' token reference; none found.");
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// A bare <c>_</c>-prefixed value on a **string-typed** component parameter is a literal, not the
    /// field: <c>Value="_model.Body"</c> binds the text “_model.Body” and silently disconnects the
    /// input — it compiles, the page renders, and only a human notices the editor is dead (Q49b,
    /// 2026-10-21). It is guarded here beside the other markup invariants because it fails silently.
    /// <para>
    /// The parameter's TYPE is what decides: on a non-string parameter Blazor evaluates the value as a
    /// C# expression, so <c>Contacts="_model.Contacts"</c> is correct and must not be flagged. The
    /// guard therefore resolves each repo component's declared string parameters first and flags only
    /// those. Third-party FluentUI parameters are the recorded exclusion — they are not declared in
    /// this tree, so the guard cannot see their types.
    /// </para>
    /// </summary>
    [TestMethod]
    public void NoBareFieldLiteral_OnAStringTypedComponentParameter()
    {
        var stringParameters = StringParametersByComponent();
        var failures = new List<string>();

        foreach (var path in RazorFiles())
        {
            var text = StripRazorComments(File.ReadAllText(path));
            foreach (Match tag in Regex.Matches(text, ComponentTagPattern))
            {
                var component = tag.Groups[1].Value;
                if (!stringParameters.TryGetValue(component, out var names))
                {
                    continue;
                }

                foreach (Match attribute in Regex.Matches(tag.Groups[2].Value, BareFieldLiteralPattern))
                {
                    if (!names.Contains(attribute.Groups[1].Value))
                    {
                        continue;
                    }

                    var line = text.Take(tag.Index).Count(c => c == '\n') + 1;
                    failures.Add(
                        $"{Relative(path)}:{line} — <{component} {attribute.Groups[1].Value}=" +
                        $"\"{attribute.Groups[2].Value}\"> binds the LITERAL, not the field. Razor needs " +
                        "the leading '@' to evaluate the expression.");
                }
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    /// <summary>Component name → the names of its <c>[Parameter] … string</c> properties.</summary>
    private static Dictionary<string, HashSet<string>> StringParametersByComponent()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in SourceFiles([".razor", ".cs"]))
        {
            var name = Path.GetFileName(file);
            var component = name.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase)
                ? name[..^".razor.cs".Length]
                : Path.GetFileNameWithoutExtension(name);

            foreach (Match match in Regex.Matches(File.ReadAllText(file), StringParameterPattern))
            {
                if (!map.TryGetValue(component, out var names))
                {
                    map[component] = names = new HashSet<string>(StringComparer.Ordinal);
                }

                names.Add(match.Groups[1].Value);
            }
        }

        return map;
    }
}
