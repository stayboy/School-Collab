using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.MigrationService.Tests.Unit;

/// <summary>
/// AC7 (revised) — seed hygiene for the grade↔stream move. The <c>gradeLevel</c>
/// attribute stopped being the link, so it must stop being seeded and its definition
/// must go; the surviving <c>streamVersion</c> attribute rows are pinned
/// row-count-wise, and the deleted CSV mapping is asserted absent (owner decisions
/// "configured grades only" + "manual linking").
/// </summary>
[TestClass]
public class SeedDataHygieneTests
{
    private static string SeedDataFile(string fileName)
    {
        var asmDir = Path.GetDirectoryName(typeof(SeedDataHygieneTests).Assembly.Location)!;
        var repoRoot = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "SchoolCollab.MigrationService", "SeedData", fileName);
    }

    private static string[] DataRows(string fileName)
    {
        var path = SeedDataFile(fileName);
        File.Exists(path).Should().BeTrue($"the seed file should exist at '{path}'");
        return File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Skip(1) // header
            .ToArray();
    }

    [TestMethod]
    public void SeedAttributes_ContainsNoGradeLevelRowsForStreams()
    {
        var rows = DataRows("seed-attributes.csv");

        rows.Where(r => r.Split(',') is [var code, var key, ..]
                        && key == "gradeLevel"
                        && code.StartsWith("GRSTREAMS", StringComparison.Ordinal))
            .Should().BeEmpty(
                "the grade↔stream link is the bridge row now — the attribute must not be seeded on any stream");
    }

    [TestMethod]
    public void SeedStreamAssignmentsFile_IsRemoved()
    {
        // Owner decisions "configured grades only" + "manual linking" made the CSV
        // mapping dead: a fresh database has no grade_levels rows to attach to, and a
        // newly configured grade must not get auto-linked streams.
        var path = SeedDataFile("seed-stream-assignments.csv");
        File.Exists(path).Should().BeFalse(
            "the native grade↔stream pre-seed was removed with the preservation-only seeder");
    }

    [TestMethod]
    public void SeedAttributes_KeepsTheStreamVersionRows()
    {
        var rows = DataRows("seed-attributes.csv");

        rows.Count(r => r.Split(',') is [var code, var key, ..]
                        && key == "streamVersion"
                        && code.StartsWith("GRSTREAMS", StringComparison.Ordinal))
            .Should().Be(39, "the streamVersion attribute stays — only the gradeLevel link was superseded");
    }

    [TestMethod]
    public void SeedAttributeDefinitions_DropsOnlyTheGradeLevelDefinition()
    {
        var rows = DataRows("seed-attribute-definitions.csv");

        rows.Should().NotContain(r => r.StartsWith("GRSTREAMS,gradeLevel,", StringComparison.Ordinal),
            "the gradeLevel definition drove editability in the coded-values landing; keeping it would invite writes");
        rows.Should().Contain(r => r.StartsWith("GRSTREAMS,streamVersion,", StringComparison.Ordinal));
    }
}
