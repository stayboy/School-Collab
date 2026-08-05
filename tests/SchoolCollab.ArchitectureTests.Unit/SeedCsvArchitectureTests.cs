using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the guardian-plan seed data (spec §16 Phase 1): the CodedValue
/// parents Relationship/Salutation/Community/City/Country, the five Accra
/// communities, and the community→city→country attribute hierarchy must be
/// present in the MigrationService seed CSVs. Reads the CSVs directly (no DB
/// or MigrationService reference) so it fails fast on missing seed data.
/// </summary>
[TestClass]
public class SeedCsvArchitectureTests
{
    private static readonly string SeedDataDir = FindSeedDataDir();

    [TestMethod]
    public void SeedCsv_ContainsGuardianCodedValueParents()
    {
        var codes = ReadCodes(Path.Combine(SeedDataDir, "seed.csv"));

        codes.Should().Contain("RELATSHIPS", "Relationship is a guardian coded-value parent (spec §4.8).");
        codes.Should().Contain("SALUTS", "Salutation is a guardian coded-value parent (spec §4.8).");
        codes.Should().Contain("COMMUNITYS", "Community is a guardian coded-value parent (spec §4.8).");
        codes.Should().Contain("CITIES", "City is a guardian coded-value parent (spec §4.8).");
        codes.Should().Contain("COUNTRYS", "Country is a guardian coded-value parent (spec §4.8).");
    }

    [TestMethod]
    public void SeedCsv_ContainsTeacherRoleCodedValueParentAndChildren()
    {
        var codes = ReadCodes(Path.Combine(SeedDataDir, "seed.csv"));

        codes.Should().Contain("TCHROLES",
            "Teacher roles is a coded-value parent for the grade-level teacher-role tag (grade-level-detail-view-plan.md §4.2).");
        codes.Should().Contain("TCHROLE_HOG");
        codes.Should().Contain("TCHROLE_CT");
        codes.Should().Contain("TCHROLE_AT");
        codes.Should().Contain("TCHROLE_SL");
    }


    [TestMethod]
    public void SeedCsv_ContainsFiveAccraCommunities_AndAccraGhana()
    {
        var codes = ReadCodes(Path.Combine(SeedDataDir, "seed.csv"));

        codes.Should().Contain("COMMUNITYS_LAPAZ");
        codes.Should().Contain("COMMUNITYS_ACHIMOTA");
        codes.Should().Contain("COMMUNITYS_EAST_LEGON");
        codes.Should().Contain("COMMUNITYS_ADENTA");
        codes.Should().Contain("COMMUNITYS_HAATSO");
        codes.Should().Contain("CITIES_ACCRA");
        codes.Should().Contain("COUNTRYS_GHANA");
    }

    [TestMethod]
    public void SeedAttributeDefinitions_ContainCityOnCommunity_AndCountryOnCity()
    {
        var defs = ReadAttributeDefinitions(Path.Combine(SeedDataDir, "seed-attribute-definitions.csv"));

        defs.Should().Contain(d => d.ParentCode == "COMMUNITYS" && d.Key == "City",
            "each community references its city (spec §4.8).");
        defs.Should().Contain(d => d.ParentCode == "CITIES" && d.Key == "Country",
            "each city references its country (spec §4.8).");
    }

    [TestMethod]
    public void SeedAttributes_LinkCommunitiesToAccra_AndAccraToGhana()
    {
        var attrs = ReadAttributes(Path.Combine(SeedDataDir, "seed-attributes.csv"));
        var communities = new[]
        {
            "COMMUNITYS_LAPAZ", "COMMUNITYS_ACHIMOTA", "COMMUNITYS_EAST_LEGON",
            "COMMUNITYS_ADENTA", "COMMUNITYS_HAATSO"
        };

        foreach (var community in communities)
        {
            attrs.Should().Contain(a => a.Code == community && a.Key == "City" && a.Value == "CITIES_ACCRA",
                $"{community} must resolve to city Accra.");
        }

        attrs.Should().Contain(a => a.Code == "CITIES_ACCRA" && a.Key == "Country" && a.Value == "COUNTRYS_GHANA",
            "city Accra must resolve to country Ghana.");
    }

    private static ISet<string> ReadCodes(string path)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var code = line.Split(',')[0].Trim();
            if (code.Length > 0) set.Add(code);
        }
        return set;
    }

    private static IReadOnlyList<(string ParentCode, string Key)> ReadAttributeDefinitions(string path)
    {
        var list = new List<(string, string)>();
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var f = line.Split(',');
            if (f.Length < 2) continue;
            list.Add((f[0].Trim().ToUpperInvariant(), f[1].Trim()));
        }
        return list;
    }

    private static IReadOnlyList<(string Code, string Key, string Value)> ReadAttributes(string path)
    {
        var list = new List<(string, string, string)>();
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var f = line.Split(',');
            if (f.Length < 3) continue;
            list.Add((f[0].Trim().ToUpperInvariant(), f[1].Trim(), f[2].Trim()));
        }
        return list;
    }

    private static string FindSeedDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir.FullName, "src", "SchoolCollab.MigrationService", "SeedData")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate src/SchoolCollab.MigrationService/SeedData from " + AppContext.BaseDirectory);
        }

        return Path.Combine(dir.FullName, "src", "SchoolCollab.MigrationService", "SeedData");
    }
}
