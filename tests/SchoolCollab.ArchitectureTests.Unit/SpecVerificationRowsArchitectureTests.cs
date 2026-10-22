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
/// A spec's verification table names the test classes that are supposed to prove it — and on this
/// repo's own record that table drifted twice in one round: §8 named
/// <c>TemplateEditorPreviewTests</c>, which never existed, and <c>NotificationTemplateTenancyTests</c>,
/// which shipped under a different name. Both survived a full Tier-3 round because nothing compared
/// the names against the tree (Q49a, 2026-10-21 — lesson L4).
/// <para>
/// The rule: every backticked <c>…Tests</c> class named in <c>documents/specs/*.md</c> exists as a
/// class under <c>tests/</c>. A row that is deliberately not implemented yet says <c>planned</c> on the
/// same line — the only escape, and the one the Q49 record allows for pre-existing aspirational rows.
/// </para>
/// </summary>
[TestClass]
public class SpecVerificationRowsArchitectureTests
{
    private const string TestsClassPattern = "`([A-Za-z][A-Za-z0-9_]*Tests)`";
    private const string DeclaredClassPattern = @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)";
    private const string PlannedMarker = "planned";

    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        while (Path.GetDirectoryName(dir) is { } parent && !File.Exists(Path.Combine(dir, "SchoolCollab.slnx")))
        {
            dir = parent;
        }

        return dir;
    }

    [TestMethod]
    public void EveryTestClassNamedInASpec_ExistsUnderTests()
    {
        var specDir = Path.Combine(RepoRoot, "documents", "specs");
        Directory.Exists(specDir).Should().BeTrue("the specs folder is the source of these rows");

        var named = NamedTestClasses(specDir);
        var declared = DeclaredClassesUnder(Path.Combine(RepoRoot, "tests"));

        var missing = named.Except(declared, StringComparer.Ordinal).ToArray();

        missing.Should().BeEmpty(
            "a spec's verification row must name a class that exists on disk, or say `planned` on the "
            + "same line (Q49a). Missing: " + string.Join(", ", missing));
    }

    /// <summary>Every backticked <c>…Tests</c> name in a spec's verification **table row**, minus the rows marked <c>planned</c>.</summary>
    private static SortedSet<string> NamedTestClasses(string specDir)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var spec in Directory.EnumerateFiles(specDir, "*.md", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(spec))
            {
                // Table rows only: prose (and a decision table's prose cells) is not a verification row.
                if (!line.TrimStart().StartsWith('|')
                    || line.Contains(PlannedMarker, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (Match match in Regex.Matches(line, TestsClassPattern))
                {
                    names.Add(match.Groups[1].Value);
                }
            }
        }

        return names;
    }

    /// <summary>Every class declared anywhere under <c>tests/</c> — one pass, whatever the file name.</summary>
    private static HashSet<string> DeclaredClassesUnder(string testsDir)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("\\obj\\", StringComparison.Ordinal)
                || file.Contains("\\bin\\", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in Regex.Matches(File.ReadAllText(file), DeclaredClassPattern))
            {
                declared.Add(match.Groups[1].Value);
            }
        }

        return declared;
    }
}
