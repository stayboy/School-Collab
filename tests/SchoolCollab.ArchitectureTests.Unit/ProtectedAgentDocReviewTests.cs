using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// <b>A governing document only lands after a higher model has reviewed the exact text that lands.</b>
///
/// <para>The rule exists because it was broken: on 2026-10-27 the agent edited
/// <c>.pi/skills/orchestrator-worker-reviewer/SKILL.md</c> mid-round with no review by anyone, and in the
/// same hour made two confident factual claims about this repo (a count and an absence) that a stronger
/// reader disproved in minutes. A governing document is what the next agent obeys <i>before</i> it reads
/// any code, so an unreviewed edit to one propagates silently into every later session.</para>
///
/// <para><b>Scope is the folder, not the entry point (owner, 2026-10-27 Q66).</b> The first draft protected
/// only <c>SKILL.md</c>; within the hour the agent had made six further edits to
/// <c>references/models.md</c> — which carries every model pin, the tier ladder and the acceptance rule —
/// and two of them were copies of a falsehood it had already "fixed" in <c>SKILL.md</c>. A guard over the
/// table of contents does not protect the book.</para>
///
/// <para><b>The mechanism is a content hash, not a commit trailer.</b> A trailer survives squash-merge and
/// means nothing when written by the same agent that wrote the change; a diff check needs history that a
/// shallow CI clone does not have. Attesting the SHA-256 of the reviewed text is the one form that can
/// neither drift from the content it vouches for nor vanish in a rebase — edit the file after the review
/// and the build fails, whatever the branch history looks like.</para>
///
/// <para><b>Reviewer order (owner, 2026-10-27 Q59):</b> <c>deepseek-v4-pro</c> build <c>0813</c> is the
/// first choice; <c>glm-5.3</c> is admissible <i>only</i> when no provider answers with that build, and
/// then only with the unavailability stated in <c>fallbackReason</c>. A bare <c>deepseek-v4-pro</c> does not
/// qualify: the registry carries several builds, so a suffix-less id is not evidence of which one reviewed.
/// "Unavailable" means no provider answers — credit exhaustion, HTTP 429, timeouts — never merely "absent
/// from a registry listing", which is how the two pools this repo cannot use still look healthy.</para>
///
/// <para><b>What this guard does NOT do — stated so nobody mistakes it for more (owner Q69).</b> It prevents
/// <i>silent drift</i>: text that changed after its review. It cannot prevent an agent editing a document
/// and its record together, because the record cannot hash itself. The control for that is the review — a
/// human reading the diff sees an unexplained hash change next to a plausible-looking edit, and the
/// <c>reason</c> is the claim that then has to hold up. The hash is a tripwire, not a signature.</para>
/// </summary>
[TestClass]
public class ProtectedAgentDocReviewTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string AttestationRelativePath = "documents/agents/protected-doc-reviews.json";

    /// <summary>Individual documents under review-gate protection.</summary>
    private static readonly string[] ProtectedFiles = ["AGENTS.md"];

    /// <summary>Folders whose <b>every</b> markdown file is protected — the skill is a folder, because its
    /// reference files carry the same binding rules as its entry point.</summary>
    private static readonly string[] ProtectedFolders = [".pi/skills/orchestrator-worker-reviewer"];

    private const string FirstChoiceModel = "deepseek-v4-pro";
    private const string FirstChoiceBuild = "0813";
    private const string FallbackModel = "glm-5.3";

    /// <summary>The folder held four files when the guard was written; a shrink to fewer is a rename or a
    /// deletion that would make the enumeration vacuous rather than protected.</summary>
    private const int ProtectedFolderMinimum = 4;

    [TestMethod]
    public void ProtectedSet_IsNotEmpty_AndStillCarriesTheSkillEntryPoint()
    {
        var paths = ProtectedPaths();

        paths.Should().Contain("AGENTS.md");
        paths.Should().Contain(".pi/skills/orchestrator-worker-reviewer/SKILL.md",
            "the enumeration must still find the folder's entry point — a rename must not quietly empty the guard");

        var folderFiles = paths.Count(p => p.StartsWith(ProtectedFolders[0] + "/", StringComparison.Ordinal));
        folderFiles.Should().BeGreaterThanOrEqualTo(ProtectedFolderMinimum,
            $"the skill folder held {ProtectedFolderMinimum} markdown files when this guard was written");
    }

    [TestMethod]
    public void EveryProtectedDoc_HasAnAttestation()
    {
        var recorded = Attestations().Select(x => x.Path).ToList();
        var missing = ProtectedPaths().Where(p => !recorded.Contains(p)).ToList();

        missing.Should().BeEmpty(
            "every protected document needs a review recorded against its current text — add an entry to " +
            $"`{AttestationRelativePath}` after the review, never before");
    }

    [TestMethod]
    public void AttestationHash_MatchesTheTextThatWouldLand()
    {
        var byPath = Attestations().ToDictionary(x => x.Path, StringComparer.Ordinal);

        foreach (var path in ProtectedPaths())
        {
            if (!byPath.TryGetValue(path, out var entry)) continue; // covered by EveryProtectedDoc_HasAnAttestation

            var actual = AttestationHash(File.ReadAllText(Path.Combine(RepoRoot, path)));

            actual.Should().Be(entry.Sha256,
                $"{path} changed after it was reviewed. Re-review the edit with {FirstChoiceModel} " +
                $"(build {FirstChoiceBuild}) and update `{AttestationRelativePath}` with the new hash — " +
                "never edit the hash alone");
        }
    }

    [TestMethod]
    public void Reviewer_IsTheFirstChoice_OrTheFallbackWithAStatedReason()
    {
        foreach (var entry in Attestations())
        {
            if (entry.Model.Equals(FallbackModel, StringComparison.OrdinalIgnoreCase))
            {
                entry.FallbackReason.Should().NotBeNullOrWhiteSpace(
                    $"{entry.Path} was reviewed by the fallback {FallbackModel}; the record must say why " +
                    $"{FirstChoiceModel} build {FirstChoiceBuild} could not be used");
                continue;
            }

            entry.Model.Should().Be(FirstChoiceModel,
                $"{entry.Path} must be reviewed by {FirstChoiceModel} first; {FallbackModel} is the fallback, " +
                "and nothing else is admissible");
            entry.Build.Should().Be(FirstChoiceBuild,
                $"a suffix-less `{FirstChoiceModel}` does not identify which build reviewed {entry.Path}");
        }
    }

    [TestMethod]
    public void Reason_NamesWhatChanged_WhyNow_AndWhatWasRejected()
    {
        foreach (var entry in Attestations())
        {
            entry.Reason.Should().ContainEquivalentOf("changes:",
                $"the reason for {entry.Path} must say what ruling changes");
            entry.Reason.Should().ContainEquivalentOf("why:",
                $"the reason for {entry.Path} must say why the change goes in now");
            entry.Reason.Should().ContainEquivalentOf("rejected:",
                $"the reason for {entry.Path} must say what alternative was rejected — the reviewer needs " +
                "something falsifiable to check the edit against");
        }
    }

    [TestMethod]
    public void NoOrphanAttestations()
    {
        var orphans = Attestations().Select(x => x.Path)
            .Where(p => !ProtectedPaths().Contains(p, StringComparer.Ordinal)).ToList();

        orphans.Should().BeEmpty(
            "an entry for a path this guard does not protect is either a stale protection that was quietly " +
            "dropped or a typo that disables the real one");
    }

    [TestMethod]
    public void HashNormalisation_IgnoresLineEndings_ButNotContent()
    {
        AttestationHash("a\r\nb\r\n").Should().Be(AttestationHash("a\nb\n"),
            "a checkout that rewrites line endings is not a content change and must not force a re-review");
        AttestationHash("a\nb\n").Should().Be(AttestationHash("a  \nb\t\n"),
            "trailing whitespace is not a content change");
        AttestationHash("a\nb\n").Should().NotBe(AttestationHash("a\nc\n"),
            "a real content change must change the hash — otherwise the guard protects nothing");
    }

    [TestMethod]
    public void HashComparison_RejectsAWrongHash()
    {
        var entry = Attestations().First(x => ProtectedPaths().Contains(x.Path, StringComparer.Ordinal));
        var tampered = entry.Sha256[..^1] + (entry.Sha256[^1] == 'a' ? 'b' : 'a');

        tampered.Should().NotBe(entry.Sha256, "the comparison this guard relies on must discriminate");
        AttestationHash(File.ReadAllText(Path.Combine(RepoRoot, entry.Path))).Should().NotBe(tampered);
    }

    // ── the protected set ───────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> ProtectedPaths()
    {
        var paths = new List<string>(ProtectedFiles);

        foreach (var folder in ProtectedFolders)
        {
            var root = Path.Combine(RepoRoot, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root)) continue;

            paths.AddRange(Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
                .Select(full => Path.GetRelativePath(RepoRoot, full).Replace(Path.DirectorySeparatorChar, '/')));
        }

        return paths.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    // ── record access ───────────────────────────────────────────────────────────────────────────────

    private sealed record Attestation(
        string Path, string Sha256, string Model, string? Build, string ReviewedBy,
        string Reason, string? FallbackReason);

    private static IReadOnlyList<Attestation> Attestations()
    {
        var path = Path.Combine(RepoRoot, AttestationRelativePath);
        File.Exists(path).Should().BeTrue($"the review record is missing: {AttestationRelativePath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        doc.RootElement.TryGetProperty("reviews", out var reviews).Should().BeTrue(
            $"`{AttestationRelativePath}` must carry a `reviews` array");

        return reviews.EnumerateArray().Select(r => new Attestation(
            Text(r, "path"), Text(r, "sha256"), Text(r, "model"), Text(r, "build"),
            Text(r, "reviewedBy"), Text(r, "reason"), Text(r, "fallbackReason"))).ToList();
    }

    private static string Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    // ── the hash ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// SHA-256 of the reviewed text, with line endings normalised to <c>\n</c>, trailing whitespace per line
    /// removed, and a leading BOM dropped. Those three are checkout artefacts; everything else counts.
    /// </summary>
    internal static string AttestationHash(string text)
    {
        var normalised = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\uFEFF');
        normalised = string.Join('\n', normalised.Split('\n').Select(line => line.TrimEnd()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised))).ToLowerInvariant();
    }

    private static string FindRepoRoot()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", ".."));
    }
}
