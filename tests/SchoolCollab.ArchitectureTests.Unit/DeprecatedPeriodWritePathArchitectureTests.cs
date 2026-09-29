using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the retired whitelist period write path (subject-period-exception-model.md §6):
/// <c>PUT /topic-assignments/{id}/period</c> is <b>deprecated, not removed</b> — the
/// endpoint and its client method stay for wire back-compat, but nothing under
/// <c>src/</c> may call them.
///
/// <para>When <c>TopicPeriodsEditDialog</c> was deleted (round subject-period-blocks,
/// 2026-09-26), <c>StudentsApiClient.UpdateTopicAssignmentPeriodAsync</c> lost its last
/// caller. That is AC-5's invariant: no UI path issues the deprecated PUT. Before this
/// guard nothing mechanically enforced it — the dialog was the only caller, and its
/// tests died with the dialog. Two failure modes are turned into a red build:</para>
///
/// <list type="bullet">
/// <item>A component re-wires the deprecated endpoint — any occurrence of the client
///       method under <c>src/</c> outside its own definition → the caller test fails.</item>
/// <item>Someone deletes the endpoint outright, breaking the deprecate-don't-delete
///       posture (spec §6 says deprecated, NOT removed) → the retention test fails.</item>
/// </list>
///
/// <para>Scope: production code under <c>src/</c> only — a test that deliberately
/// exercises the wire-compat endpoint does not violate the invariant, and build
/// output (<c>bin/</c>, <c>obj/</c>) is excluded so a stale generated artifact can
/// never fail this guard.</para>
/// </summary>
[TestClass]
public class DeprecatedPeriodWritePathArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [TestMethod]
    public void DeprecatedPeriodPutClient_HasNoProductionCallers()
    {
        // The client method is the single choke point for the deprecated PUT, so the
        // guard is a source scan: the token may occur exactly once under src/ — its
        // definition in StudentsApiClient — and nowhere else (a second occurrence
        // inside StudentsApiClient.cs itself would be an internal delegating call,
        // and is caught by the same single-occurrence count).
        var hits = new List<string>();
        foreach (var file in ScanSrc("*.cs", "*.razor"))
        {
            foreach (var line in File.ReadAllLines(file))
            {
                if (line.Contains("UpdateTopicAssignmentPeriodAsync"))
                {
                    hits.Add(file);
                }
            }
        }

        hits.Should().ContainSingle(
            "the deprecated period-PUT client method must exist exactly once — its definition — and have zero production callers");
        hits.Single().Replace('\\', '/').Should().EndWith(
            "Students.Application/Services/StudentsApiClient.cs",
            "the single permitted occurrence is the definition itself, not a call site");
    }

    [TestMethod]
    public void DeprecatedPeriodPutEndpoint_IsRetained_AndMarkedDeprecated()
    {
        // Spec §6: the endpoint is deprecated, NOT removed — deleting it outright would
        // break the deprecate-don't-delete posture the round is built on.
        var routes = Path.Combine(RepoRoot,
            "src", "Students", "SchoolCollab.Students.Api", "Endpoints", "TopicAssignmentRoutes.cs");

        File.Exists(routes).Should().BeTrue("the (deprecated) route file must still exist");

        var source = File.ReadAllText(routes);
        source.Should().Contain("/topic-assignments/{id:guid}/period",
            "the deprecated PUT route must remain mapped for wire back-compat (spec §6)");
        source.Should().Contain("DEPRECATED",
            "the route must carry the deprecation marker so the posture is visible at the seam");
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static IEnumerable<string> ScanSrc(params string[] patterns)
    {
        var src = Path.Combine(RepoRoot, "src");
        var files = new List<string>();
        foreach (var pattern in patterns)
        {
            files.AddRange(Directory.EnumerateFiles(src, pattern, SearchOption.AllDirectories));
        }

        // bin/ and obj/ are excluded on purpose: Razor/source generators write
        // generated .cs files into obj/, and a stale artifact there must never be
        // able to fail (or mask) this guard.
        return files
            .Distinct()
            .Where(f => !IsUnderBuildOutput(f));
    }

    private static bool IsUnderBuildOutput(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(path)!); dir is not null; dir = dir.Parent!)
        {
            if (dir.Name is "bin" or "obj")
            {
                return true;
            }
        }

        return false;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SchoolCollab.slnx")))
        {
            dir = dir.Parent!;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root (SchoolCollab.slnx) from " + AppContext.BaseDirectory);
        }

        return dir.FullName;
    }
}