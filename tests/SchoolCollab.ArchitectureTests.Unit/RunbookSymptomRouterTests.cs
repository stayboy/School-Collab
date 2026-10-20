using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the runbook router adopted in the optimisation round (grill Q102).
/// <para>
/// The router is deliberately <b>free</b>: an agent asks "which runbook covers this symptom?" by
/// running <c>grep -l '^## Symptom' documents/runbooks/*.md</c>, and the filesystem is the index —
/// no stored manifest to keep in sync. That only works if <b>every</b> runbook carries a
/// <c>## Symptom</c> block: the convention existed in 1 of 4 runbooks when this was written, so a
/// router built then would have invented content for the other three and reported itself correct.
/// </para>
/// <para>
/// The guard is therefore one-directional and total (every runbook has the block), which is what
/// the grep router needs. A stored <c>README</c> index was rejected precisely because it needs a
/// <i>bidirectional</i> test — the second taxonomy the owner declined.
/// </para>
/// </summary>
[TestClass]
public class RunbookSymptomRouterTests
{
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AppHost", "SchoolCollab.AppHost")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName;
    }

    [TestMethod]
    public void EveryRunbook_CarriesASymptomBlock_SoTheGrepRouterCannotLie()
    {
        var root = FindRepoRoot();
        Assert.IsNotNull(root, "Could not locate the repository root from " + AppContext.BaseDirectory);

        var runbookDir = Path.Combine(root, "documents", "runbooks");
        Directory.Exists(runbookDir).Should().BeTrue($"{runbookDir} must exist");

        var runbooks = Directory.GetFiles(runbookDir, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        // Non-vacuity: a router test that scans zero files passes for the wrong reason.
        runbooks.Should().NotBeEmpty("the runbook folder must contain at least one runbook");

        var missing = runbooks
            .Where(f => !File.ReadLines(f).Any(line => line.TrimEnd() == "## Symptom"))
            .Select(f => Path.GetFileName(f))
            .ToArray();

        missing.Should().BeEmpty(
            "every runbook needs a '## Symptom' block — it is the only field the free router " +
            "(`grep -l '^## Symptom' documents/runbooks/*.md`) can search, so a runbook without one " +
            "is invisible to every future investigation");
    }

    [TestMethod]
    public void TheGrepRouterItself_FindsEveryRunbook()
    {
        // Pins the *documented* discovery command to the same answer as the file scan above:
        // if someone changes the heading (e.g. to '## Symptoms' or '## Symptom(s)'), the anchor
        // in the skill and the runbooks' own headings must move together.
        var root = FindRepoRoot();
        Assert.IsNotNull(root, "Could not locate the repository root from " + AppContext.BaseDirectory);

        var runbookDir = Path.Combine(root, "documents", "runbooks");
        var anchored = Directory.GetFiles(runbookDir, "*.md", SearchOption.TopDirectoryOnly)
            .Where(f => File.ReadLines(f).Any(line => line.StartsWith("## Symptom", StringComparison.Ordinal)))
            .ToArray();

        var all = Directory.GetFiles(runbookDir, "*.md", SearchOption.TopDirectoryOnly);

        anchored.Should().HaveCount(all.Length,
            "`grep -l '^## Symptom' documents/runbooks/*.md` must return every runbook — " +
            "the router's anchor is a contract, not a formatting preference");
    }
}
