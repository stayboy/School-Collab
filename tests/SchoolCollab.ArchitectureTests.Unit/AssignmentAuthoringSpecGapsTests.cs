using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// D7 (round <c>authoring-ux7-residuals</c>): the durable spec carries the DEFERRED residual list.
/// <para>
/// This is a source-inspection guard of the kind only this project can hold: the invariants live in a
/// document rather than in code, so no unit test of the authoring surface can see them. The reason
/// the section has to exist at all is the repo's own folder policy — <c>documents/rounds/</c> is
/// declared ephemeral (<c>documents/rounds/README.md</c>), so a residual "recorded" only in a round doc
/// is deleted the next time the folder is bulk-trashed. The spec is the durable home: an open gap
/// someone can still act on, in the file a reader consults before changing this page.
/// </para>
/// <para>
/// The assertions deliberately check for FACTS about the entries (the residual's identifier, the
/// mechanism that makes it unproven, where the proof has to live, and the other carried-over gaps)
/// rather than for prose, so the section can be reworded freely while still having to stay honest.
/// </para>
/// </summary>
[TestClass]
public class AssignmentAuthoringSpecGapsTests
{
    private static readonly string SpecPath = Path.Combine(
        FindRepoRoot(), "documents", "specs", "assignment-authoring-compartments.md");

    private static readonly string Spec = File.ReadAllText(SpecPath);

    /// <summary>Everything after the "Deferred / known gaps" heading, up to the next level-2 heading —
    /// the section under test, so an assertion cannot be satisfied by text from a neighbour.</summary>
    private static readonly string GapsSection = ExtractSection(Spec);

    /// <summary>The gaps section is present, uniquely, and is a level-2 section of the durable spec.</summary>
    [TestMethod]
    public void Spec_CarriesTheDeferredGapsSection()
    {
        Regex.Matches(Spec, @"^## \d+\. Deferred / known gaps\r?$", RegexOptions.Multiline)
            .Should().ContainSingle(
                "D7: the residual list needs a durable home — documents/rounds/ is ephemeral, so a gap " +
                "recorded only there is deleted with the folder");

        GapsSection.Length.Should().BeGreaterThan(400,
            "a one-line section is not an actionable record: an open gap has to say what is missing and " +
            "what closing it takes");
    }

    /// <summary>The section names the still-open residuals with the context that makes them
    /// actionable — at minimum the P2-c transaction-rollback proof the round deferred, plus the other
    /// residuals carried forward rather than dropped.</summary>
    [TestMethod]
    public void DeferredGapsSection_NamesTheOpenResidualsWithTheirActionableContext()
    {
        GapsSection.Should().Contain("P2-c",
            "the transaction-rollback proof is the residual the round explicitly deferred");
        GapsSection.Should().Contain("rollback",
            "and the entry has to say WHAT is unproven, not merely carry an identifier");
        GapsSection.Should().Contain("Integration",
            "the proof cannot live in the InMemory unit fixture, so the entry names the suite that can " +
            "actually hold it");
        GapsSection.Should().Contain("archived",
            "the R1 archived-group relink residual travels with this page and must not be silently dropped");
    }

    private static string ExtractSection(string spec)
    {
        var match = Regex.Match(spec, @"^## \d+\. Deferred / known gaps\r?$(?<body>.*?)^## ", RegexOptions.Multiline | RegexOptions.Singleline);
        return match.Success ? match.Groups["body"].Value : string.Empty;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "documents", "specs")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the repo root (documents/specs) from " + AppContext.BaseDirectory);
    }
}
