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
/// <b>A caught exception's <c>Message</c> never reaches the UI.</b> Raw provider text in a message bar
/// leaks socket errors, stack frames, SQL details and internal type names to teachers, guardians and
/// administrators (#416), and it does so invisibly: the code compiles, the page renders, the error looks
/// like an error. The fix is one seam — log the exception with full detail, show the site's own plain
/// sentence (<c>FriendlyError.Describe</c> + <c>FriendlyErrorBar</c>) — and this scan is what keeps a new
/// <c>_error = ex.Message</c> from landing beside it.
///
/// <para><b>What it detects, and what it deliberately does not.</b> The defect is the SOURCE, not the
/// sink: a <c>Message</c> read off a caught exception (<c>ex.Message</c>, <c>$"… {ex.Message}"</c>,
/// <c>Error = ex.Message</c>). It does NOT flag <c>@_error</c> / <c>@_message</c> renders — that is the
/// field the fix keeps, and flagging it would both redden corrected code and be defeated by renaming the
/// field to <c>_msg</c>. Nor does it flag <c>HttpResponseMessage</c>/<c>ProblemDetails</c> text the UI is
/// meant to show, or a model property that happens to be called <c>Message</c>: only identifiers bound by
/// a <c>catch (T ident)</c> clause or an <c>&lt;ErrorContent Context="ident"&gt;</c> fragment count as
/// caught exceptions.</para>
///
/// <para><b>Scope is the whole tree, not the swept pages.</b> The rule was ratified as a pattern
/// (owner Q52/Q58a), and the precedent is <see cref="DropdownWidthFloorArchitectureTests"/>, which scans
/// every <c>src/**/*.razor</c> precisely so a fix cannot be quietly re-broken two modules away. The
/// remaining sites are enumerated in <see cref="FriendlyErrorAllowList"/>, whose length is ratcheted:
/// the ledger may shrink, never grow.</para>
///
/// <para><b>There are no <c>*.razor.cs</c> code-behind files in this tree</b> (verified by the plan
/// gate), so a <c>.razor</c>-only scan covers the entire UI idiom; the only <c>.cs</c> uses are API route
/// responses (<c>new { ex.Message }</c>), a different concern.</para>
/// </summary>
[TestClass]
public class FriendlyErrorArchitectureTests
{
    private static readonly string SrcRoot = FindSrcRoot();

    /// <summary>A <c>catch</c> clause that binds the exception to a named local.</summary>
    private static readonly Regex CatchBinding =
        new(@"catch\s*\(\s*[\w\.<>\[\]]+\s+(?<id>\w+)\s*\)", RegexOptions.Compiled);

    /// <summary>
    /// An <c>ErrorBoundary</c>'s <c>ErrorContent</c> fragment, whose <c>Context</c> is also a caught
    /// exception — <c>Something went wrong: @ex.Message</c> is the same leak in markup, and it is the
    /// shape the sweep's two page surfaces carried.
    /// </summary>
    private static readonly Regex BoundaryBinding =
        new("<ErrorContent\\s[^>]*Context\\s*=\\s*\"(?<id>\\w+)\"", RegexOptions.Compiled);

    /// <summary>Razor comments are prose, not code — a commented-out site is not a violation.</summary>
    private static readonly Regex RazorComment =
        new("@\\*.*?\\*@", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>One raw-message site: where it is, which exception local it reads, and the line.</summary>
    internal sealed record RawMessageSite(string File, int Line, string Identifier, string Text);

    private static string FindSrcRoot()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "..", "src"));
    }

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.razor", SearchOption.AllDirectories)
            .Where(p => !p.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                        && !p.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase));

    private static string Relative(string path) => path.Replace(SrcRoot, "src").Replace('\\', '/');

    /// <summary>
    /// Every raw-message site in one <c>.razor</c> source. Pure (path + text in, sites out) so the
    /// negative control below can prove the detector against fixtures instead of the live tree.
    /// </summary>
    internal static IReadOnlyList<RawMessageSite> Find(string relativePath, string source)
    {
        // Blank the comments in place, keeping their newlines, so reported line numbers stay true.
        var text = RazorComment.Replace(source, m => new string('\n', m.Value.Count(c => c == '\n')));

        var identifiers = CatchBinding.Matches(text).Select(m => m.Groups["id"].Value)
            .Concat(BoundaryBinding.Matches(text).Select(m => m.Groups["id"].Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (identifiers.Length == 0)
        {
            return [];
        }

        var sites = new List<RawMessageSite>();
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var identifier in identifiers)
            {
                // `(?<![\w.])` keeps `SomeException.Message` and `ChatHub.Messages` out: only the bare
                // caught local counts, and `\bMessage\b` excludes `Messages`.
                if (Regex.IsMatch(lines[i], $@"(?<![\w.]){Regex.Escape(identifier)}\s*\.\s*Message\b"))
                {
                    sites.Add(new RawMessageSite(relativePath, i + 1, identifier, lines[i].Trim()));
                    break;
                }
            }
        }

        return sites;
    }

    /// <summary>
    /// Every remaining site must be in the ledger, and each file must hold exactly the number of
    /// allow-listed sites it still holds — so a new site fails, and a CONVERTED site fails until its
    /// ledger line is deleted (debt can only be paid down, never silently re-borrowed).
    /// </summary>
    [TestMethod]
    public void Razor_NeverRendersARawExceptionMessage()
    {
        var detected = new Dictionary<string, List<RawMessageSite>>(StringComparer.Ordinal);

        foreach (var file in RazorFiles())
        {
            var relative = Relative(file);
            var sites = Find(relative, File.ReadAllText(file));
            if (sites.Count > 0)
            {
                detected[relative] = [.. sites];
            }
        }

        var allowed = FriendlyErrorAllowList.Sites
            .Select(entry => (Entry: entry, File: ParseFile(entry)))
            .GroupBy(x => x.File, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        var problems = new List<string>();

        foreach (var (file, sites) in detected)
        {
            if (!allowed.ContainsKey(file))
            {
                problems.Add(
                    $"{file}: {sites.Count} raw-message site(s) and NO allow-list entry — "
                    + string.Join(
                        " | ",
                        sites.Select(s => $"line {s.Line}: {s.Text}")));
            }
            else if (allowed[file].Length != sites.Count)
            {
                problems.Add(
                    $"{file}: {sites.Count} detected vs {allowed[file].Length} allow-listed — "
                    + string.Join(" | ", sites.Select(s => $"line {s.Line}: {s.Text}")));
            }
        }

        foreach (var (file, entries) in allowed)
        {
            if (!detected.ContainsKey(file))
            {
                problems.Add(
                    $"{file}: {entries.Length} allow-list entr{(entries.Length == 1 ? "y" : "ies")} but no "
                    + "site remains — delete them, and lower RecordedEntryCount with them");
            }
        }

        problems.Should().BeEmpty(
            "a caught exception's Message must never reach the UI: log the exception and show a plain "
            + "sentence (FriendlyError.Describe + FriendlyErrorBar), or add the site to the ledger if it "
            + "is not this round's to convert. Problems: {0}",
            problems.Count == 0 ? "(none)" : Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// <b>The ratchet.</b> The ledger's length never exceeds the count recorded when the guard landed,
    /// so a new violation cannot be admitted by appending a line — admitting one would mean editing this
    /// constant too, which is a visible act in review rather than a quiet one.
    /// </summary>
    [TestMethod]
    public void AllowList_NeverGrows()
    {
        FriendlyErrorAllowList.Sites.Should()
            .HaveCountLessThanOrEqualTo(
                FriendlyErrorAllowList.RecordedEntryCount,
                "the allow-list may only shrink: converting a site deletes its line, and admitting a new "
                + "one would mean converting a site first (owner Q58a ratchet)");

        FriendlyErrorAllowList.Sites.Should()
            .OnlyHaveUniqueItems("a duplicated entry would hide a second site in the same file");

        foreach (var entry in FriendlyErrorAllowList.Sites)
        {
            ParseFile(entry).Should().StartWith("src/", $"'{entry}' must name a file under src/");
            ParseLine(entry).Should().BePositive($"'{entry}' must carry a 1-based line number");
        }
    }

    /// <summary>
    /// <b>The negative control: a guard never seen failing is a guard that does not work.</b> Each of the
    /// four shipped shapes of the defect must be detected in a fixture, so this test fails outright if the
    /// detector is ever weakened into vacuity (which the live-tree test alone cannot show — an empty scan
    /// passes it).
    /// </summary>
    [TestMethod]
    public void Detector_FlagsEveryShippedShapeOfTheDefect()
    {
        const string Fixture = """
            <ErrorContent Context="ex">
                <FluentMessageBar Intent="MessageBarIntent.Error">Something went wrong: @ex.Message</FluentMessageBar>
            </ErrorContent>

            @code {
                private async Task LoadAsync()
                {
                    try { _items = await Api.ListAsync(); }
                    catch (Exception ex) { _error = ex.Message; }
                }

                private async Task SaveAsync()
                {
                    try { await Api.SaveAsync(); }
                    catch (IOException io) { _error = $"Could not save: {io.Message}"; }
                }

                private async Task DeleteAsync()
                {
                    try { await Api.DeleteAsync(); }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        Error = exception.Message;
                    }
                }
            }
            """;

        var sites = Find("fixture/Forgotten.razor", Fixture);

        sites.Should().HaveCount(4, "each of the four shapes must be detected");
        sites.Should().OnlyContain(s => s.File == "fixture/Forgotten.razor");
        sites.Select(s => s.Identifier).Should().BeEquivalentTo("ex", "ex", "io", "exception");
        sites.Select(s => s.Line).Should().BeInAscendingOrder("sites are reported in source order");
        sites.Select(s => s.Text).Should().Contain(t => t.Contains("@ex.Message"),
            "the ErrorBoundary markup shape — the one a sink-shaped rule would miss");
        sites.Select(s => s.Text).Should().Contain(t => t.Contains("$\"Could not save: {io.Message}\""),
            "the interpolated shape, which no simple `@ex.Message` grep finds");
    }

    /// <summary>
    /// The corrected shapes must be silent — otherwise the guard reddens the fix itself, which is how it
    /// would get deleted instead of obeyed. Includes the renamed sink (<c>_msg</c>): the rule is about the
    /// SOURCE, so renaming must not evade it, and rendering a field must never trip it.
    /// </summary>
    [TestMethod]
    public void Detector_IgnoresTheSeamAndTheErrorFieldItRenders()
    {
        const string Corrected = """
            <ErrorContent Context="ex">
                <FriendlyErrorBar Message="@DescribeRenderError(ex)" />
            </ErrorContent>

            <FriendlyErrorBar Message="@_error" />
            <FluentMessageBar Intent="MessageBarIntent.Error">@_msg</FluentMessageBar>
            <FluentMessageBar Intent="MessageBarIntent.Error">@Content.Message</FluentMessageBar>

            @code {
                private string? _error;
                private string? _msg;

                private async Task LoadAsync()
                {
                    try { _items = await Api.ListAsync(); }
                    catch (Exception ex)
                    {
                        _error = FriendlyError.Describe(ex, "Teachers could not be loaded. Try again.", Logger);
                        _msg = _error;
                    }
                }
            }
            """;

        Find("fixture/Corrected.razor", Corrected).Should().BeEmpty(
            "the seam (FriendlyError.Describe), the error field it fills, a renamed field, and a model "
            + "property called Message are all legal — flagging any of them would redden the fix or be "
            + "defeated by a rename");
    }

    /// <summary>The file half of a <c>file:line</c> ledger entry.</summary>
    private static string ParseFile(string entry)
    {
        var colon = entry.LastIndexOf(':');
        colon.Should().BeGreaterThan(0, $"'{entry}' must be written as file:line");
        return entry[..colon];
    }

    /// <summary>The line half of a <c>file:line</c> ledger entry.</summary>
    private static int ParseLine(string entry)
    {
        var colon = entry.LastIndexOf(':');
        int.TryParse(entry[(colon + 1)..], out var line).Should().BeTrue($"'{entry}' must end in a line number");
        return line;
    }
}
