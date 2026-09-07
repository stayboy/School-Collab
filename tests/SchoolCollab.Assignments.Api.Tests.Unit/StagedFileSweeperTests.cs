using FluentAssertions;
using SchoolCollab.Assignments.Api.Services;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-A1 / decision (b) — pure <see cref="StagedFileSweeper.DeleteStaleFiles"/>
/// coverage. The BackgroundService loop is statically reviewed (no loop
/// test); this suite exercises the correctness core: old + unreferenced
/// → deleted, recent → kept, old but referenced → kept. No DbContext, no
/// BackgroundService — temp directories only.
/// </summary>
[TestClass]
public class StagedFileSweeperTests
{
    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "sc-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteFile(string path, DateTime lastWriteUtc, string content = "data")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    [TestMethod]
    public void DeleteStaleFiles_OldAndUnreferenced_IsDeleted()
    {
        var root = CreateTempRoot();
        try
        {
            var old = WriteFile(Path.Combine(root, "tenants/t/staging/g/old.pdf"),
                DateTime.UtcNow.AddDays(-3));

            var deleted = StagedFileSweeper.DeleteStaleFiles(
                root, referencedLogicalPaths: new HashSet<string>(), DateTime.UtcNow.AddDays(-1));

            deleted.Should().Be(1);
            File.Exists(old).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteStaleFiles_Recent_IsKept()
    {
        var root = CreateTempRoot();
        try
        {
            var recent = WriteFile(Path.Combine(root, "tenants/t/staging/g/recent.pdf"),
                DateTime.UtcNow.AddMinutes(-5));

            var deleted = StagedFileSweeper.DeleteStaleFiles(
                root, referencedLogicalPaths: new HashSet<string>(), DateTime.UtcNow.AddDays(-1));

            deleted.Should().Be(0);
            File.Exists(recent).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteStaleFiles_OldButReferenced_IsKept()
    {
        var root = CreateTempRoot();
        try
        {
            var referencedLogical = "tenants/t/staging/g/live.pdf";
            var referenced = WriteFile(Path.Combine(root, referencedLogical),
                DateTime.UtcNow.AddDays(-3));

            var deleted = StagedFileSweeper.DeleteStaleFiles(
                root,
                referencedLogicalPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { referencedLogical },
                DateTime.UtcNow.AddDays(-1));

            deleted.Should().Be(0, "decision (b): the reference check must keep live assignment blobs alive");
            File.Exists(referenced).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteStaleFiles_NestedTenantAndStagingFolders_AreSwept()
    {
        var root = CreateTempRoot();
        try
        {
            // Two old files in different tenant/staging subtrees; both unreferenced → both deleted.
            var nestedA = WriteFile(Path.Combine(root, "tenants/AAA/staging/gAAA/x.pdf"),
                DateTime.UtcNow.AddDays(-2));
            var nestedB = WriteFile(Path.Combine(root, "tenants/BBB/staging/gBBB/y.pdf"),
                DateTime.UtcNow.AddDays(-2));
            var recentDeep = WriteFile(Path.Combine(root, "tenants/AAA/staging/gAAA/z.pdf"),
                DateTime.UtcNow.AddMinutes(-1));

            var deleted = StagedFileSweeper.DeleteStaleFiles(
                root, referencedLogicalPaths: new HashSet<string>(), DateTime.UtcNow.AddDays(-1));

            deleted.Should().Be(2);
            File.Exists(nestedA).Should().BeFalse();
            File.Exists(nestedB).Should().BeFalse();
            File.Exists(recentDeep).Should().BeTrue("a recent file in a deep directory is still kept");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteStaleFiles_EmptyRoot_ReturnsZero()
    {
        var root = CreateTempRoot();
        try
        {
            var deleted = StagedFileSweeper.DeleteStaleFiles(
                root, referencedLogicalPaths: new HashSet<string>(), DateTime.UtcNow.AddDays(-1));

            deleted.Should().Be(0);
        }
        finally
            {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteStaleFiles_MissingRoot_ReturnsZero()
    {
        var missing = Path.Combine(Path.GetTempPath(), "sc-sweep-missing-" + Guid.NewGuid().ToString("N"));
        var deleted = StagedFileSweeper.DeleteStaleFiles(
            missing, referencedLogicalPaths: new HashSet<string>(), DateTime.UtcNow.AddDays(-1));
        deleted.Should().Be(0, "a missing root is a no-op so a not-yet-created directory never crashes the sweep");
    }

    [TestMethod]
    public void ToLogicalPath_NormalizesToForwardSlashes()
    {
        var logical = StagedFileSweeper.ToLogicalPath(
            Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            Path.Combine(Path.GetTempPath(), "tenants", "t", "staging", "g", "x.pdf"));
        logical.Should().Be("tenants/t/staging/g/x.pdf");
    }
}
