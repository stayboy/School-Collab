using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Round <c>drop-primary-grade</c> — the source-shape guard that no live seam still carries the
/// ASSIGNMENT's grade. No behavioural test can see this: the removed surface was self-consistent, and
/// its last consumers were doc comments and projections whose staleness only shows up at the next
/// author's desk.
///
/// <list type="number">
/// <item><b>The enumerated declarations must be gone.</b> The entity property, its configuration
/// mapping + index, the summary contract's <c>GradeLevelId</c>/<c>GradeName</c>, the
/// create/update request fields, the AI generation request field and the three sweep-candidate
/// fields.</item>
/// <item><b>No receiver-qualified SINGULAR read survives.</b> Every non-migration
/// <c>src/Assignments</c> file must be free of <c>«receiver».GradeLevelId</c> — the shape every
/// removed seam used (<c>a.GradeLevelId</c>, <c>assignment.GradeLevelId</c>,
/// <c>candidate.GradeLevelId</c>, …).</item>
/// </list>
///
/// <para><b>The plural is deliberately NOT flagged.</b> <c>GradeLevelIds</c>
/// (<c>IContactResolver.ResolveSubscribersRequest</c>, <c>AssignmentsApiClient</c>, the
/// <c>recipient-preview</c> route) is the KEPT teacher-cohort input of this round and must not trip the
/// gate — the round's own positive control is
/// <see cref="TheGateIsSingularOnly_SoTheKeptPluralTeacherCohortInputDoesNotTrip"/>.</para>
/// </summary>
[TestClass]
public class AssignmentGradeSeamArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>Every historical migration (and the current snapshot) legitimately describes
    /// <c>grade_level_id</c> as it was — they are immutable records, not live seams.</summary>
    private const string MigrationsFolder = "Migrations";

    /// <summary>Files allowed to name the SINGULAR <c>GradeLevelId</c> — a DIFFERENT grade concept, or a
    /// Students-grade policy seam this round deliberately leaves alone.</summary>
    private static readonly string[] AllowedGradeLevelIdFiles =
    [
        // The teacher's TAUGHT grade: the local mirror of the Students teaching rows and its wire shape.
        // A different concept from the assignment's grade, and the round does not touch it.
        Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Core", "Services", "TeacherScope.cs"),
        Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Api", "Services", "TeacherScopeHttpClient.cs"),
        Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Api", "Services", "TeacherGradeAssignmentResponse.cs"),
        // The generic grade-policy resolvers: their {GradeLevelId} LOG PLACEHOLDER names the Students
        // grade a policy is resolved for, generically.
        Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Api", "Services", "AssignmentPolicyResolver.cs"),
        Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Core", "Services", "NotificationPolicyResolver.cs"),
    ];

    /// <summary>The declarations the round removes, each pinned by file + forbidden token. A file-level
    /// token check (rather than a regex per declaration) is what makes this half falsifiable: the token
    /// must not come back anywhere in the file that owned it.</summary>
    private static readonly (string RelativePath, string[] ForbiddenTokens)[] DeclarationRules =
    [
        (Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Core", "Domain", "Assignment.cs"),
            ["GradeLevelId"]),
        (Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Core", "Data", "Configurations", "AssignmentConfiguration.cs"),
            ["GradeLevelId"]),
        (Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Core", "DTOs", "AssignmentSummary.cs"),
            ["GradeLevelId"]),
        (Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Core", "DTOs", "NotificationSweepCandidates.cs"),
            ["GradeLevelId"]),
        (Path.Combine("src", "AI", "SchoolCollab.AI.Abstractions", "AssignmentQuestionGenerationTypes.cs"),
            ["GradeLevelId"]),
    ];

    /// <summary>The assignment-facing records whose fields the round removes — checked inside their own
    /// declaration block, so a future unrelated DTO in the same file does not have to be grade-free.</summary>
    private static readonly (string RelativePath, string RecordDeclaration, string[] ForbiddenTokens)[] RecordRules =
    [
        (Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Contracts", "ContractTypes.cs"),
            "public record AssignmentSummaryDto(", ["GradeLevelId", "GradeName"]),
        (Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Contracts", "ContractTypes.cs"),
            "public record CreateAssignmentRequest(", ["GradeLevelId"]),
        (Path.Combine("src", "Assignments", "SchoolCollab.Assignments.Contracts", "ContractTypes.cs"),
            "public record UpdateAssignmentRequest(", ["GradeLevelId"]),
    ];

    /// <summary>A receiver-qualified read of the SINGULAR token — <c>a.GradeLevelId</c>,
    /// <c>assignment.GradeLevelId</c>, <c>candidate.GradeLevelId</c>. The trailing boundary keeps
    /// <c>.GradeLevelIds</c> (the kept plural) out of the match.</summary>
    private static readonly Regex ReceiverQualifiedGradeRead =
        new(@"\.GradeLevelId\b", RegexOptions.CultureInvariant);

    [TestMethod]
    public void NoLiveAssignmentGradeSeamSurvives()
    {
        foreach (var (relativePath, forbidden) in DeclarationRules)
        {
            var text = Read(relativePath);
            foreach (var token in forbidden)
            {
                text.Should().NotContain(token,
                    $"round drop-primary-grade removed the assignment's {token}; {relativePath} must not carry it again");
            }
        }

        foreach (var (relativePath, declaration, forbidden) in RecordRules)
        {
            var block = RecordBlock(Read(relativePath), declaration);
            foreach (var token in forbidden)
            {
                block.Should().NotContain(token,
                    $"round drop-primary-grade removed the {token} field of " + declaration
                    + "; the record is the wire seam for it and must not carry it again");
            }
        }

        var offenders = AssignmentSources()
            .Where(path => ReceiverQualifiedGradeRead.IsMatch(File.ReadAllText(path)))
            .Select(Relative)
            .Where(path => !AllowedGradeLevelIdFiles.Contains(path))
            .Order()
            .ToList();

        offenders.Should().BeEmpty(
            "no non-migration src/Assignments file may read the assignment's singular grade — the grade half of an "
            + "assignment is its GradeLevel target rows (AssignmentPolicyScope.GradeTargetIds / the summary's "
            + "TargetGradeIds). Allowed exceptions: " + string.Join(", ", AllowedGradeLevelIdFiles));
    }

    /// <summary>The round's anti-overreach control: the gate must not fire on the PLURAL
    /// <c>GradeLevelIds</c>, which is the teacher-cohort input this round KEEPS
    /// (<c>ResolveSubscribersRequest</c>, the preview route, the API client).</summary>
    [TestMethod]
    public void TheGateIsSingularOnly_SoTheKeptPluralTeacherCohortInputDoesNotTrip()
    {
        ReceiverQualifiedGradeRead.IsMatch("request.GradeLevelIds").Should().BeFalse(
            "GradeLevelIds is the kept teacher-cohort input — a gate that flagged it would forbid the round's own " +
            "publish path");
        ReceiverQualifiedGradeRead.IsMatch("AssignmentPolicyScope.GradeTargetIds(assignment)").Should().BeFalse();
        ReceiverQualifiedGradeRead.IsMatch("assignment.GradeLevelId").Should().BeTrue(
            "the singular receiver-qualified read is exactly what the gate exists to catch");
    }

    /// <summary>Never edit a historical migration, and never let an old designer's
    /// <c>grade_level_id</c> be "tidied" into the current shape — this pins the two records the drop
    /// round must leave alone, plus the snapshot it MUST update: the migration that runs immediately
    /// before the drop still describes the legacy column, the drop migration's own designer describes it
    /// as gone, and the CURRENT snapshot agrees with the drop.</summary>
    [TestMethod]
    public void TheLastPreDropMigrationKeepsItsGradeLevelId_AndTheDropAndSnapshotDoNot()
    {
        var migrations = Path.Combine(RepoRoot, "src", "Assignments", "SchoolCollab.Assignments.Core", MigrationsFolder);
        var designers = Directory.EnumerateFiles(migrations, "*.Designer.cs").Order().ToList();

        var dropDesigner = designers.SingleOrDefault(p => p.Contains(DropMigrationName));
        dropDesigner.Should().NotBeNull($"the round ships exactly one {DropMigrationName} migration");
        var preDropDesigner = designers.Last(p => !p.Contains(DropMigrationName));

        File.ReadAllText(preDropDesigner).Should().Contain("grade_level_id",
            "the migration that runs immediately BEFORE the drop is an immutable record of the legacy column");
        File.ReadAllText(dropDesigner!).Should().NotContain("grade_level_id",
            "…and the drop migration's own designer records the column as gone (only a NEW migration may drop it)");
        File.ReadAllText(Path.Combine(migrations, "AssignmentsDbContextModelSnapshot.cs"))
            .Should().NotContain("grade_level_id",
                "the CURRENT snapshot must describe the dropped column as gone, or has-pending-model-changes fails");
    }

    private const string DropMigrationName = "_DropAssignmentGradeLevelColumn";

    private static IEnumerable<string> AssignmentSources()
    {
        var root = Path.Combine(RepoRoot, "src", "Assignments");
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}{MigrationsFolder}{Path.DirectorySeparatorChar}"));
    }

    /// <summary>The declaration block of a record — from its declaration line to the closing
    /// <c>);</c> — so a token check is scoped to the record the round edited.</summary>
    private static string RecordBlock(string text, string declaration)
    {
        var start = text.IndexOf(declaration, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{declaration} must exist (the round edits it, never deletes it)");

        var end = text.IndexOf(");", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"{declaration} must be a closed record declaration");

        return text[start..end];
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(RepoRoot, Path.Combine(segments)));

    private static string Relative(string absolutePath) =>
        Path.GetRelativePath(RepoRoot, absolutePath);

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
