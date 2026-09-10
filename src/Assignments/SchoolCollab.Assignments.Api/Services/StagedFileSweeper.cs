namespace SchoolCollab.Assignments.Api.Services;

/// <summary>Pure file-system helper for the orphan sweep (WS-A1 / decision
/// (b)). The BackgroundService loop that calls this lives in
/// <see cref="StagedFileSweepService"/>; this class stays free of DI so
/// the unit tests can exercise it directly with temp directories.</summary>
public static class StagedFileSweeper
{
    /// <summary>Deletes files under <paramref name="root"/> that are
    /// older than <paramref name="cutoffUtc"/> AND whose logical path
    /// (full path made relative to <paramref name="root"/>, with
    /// separators normalised to forward slashes) is NOT in
    /// <paramref name="referencedLogicalPaths"/>. Returns the deleted
    /// count. Missing root is treated as "nothing to delete" (0).</summary>
    public static int DeleteStaleFiles(
        string root,
        IReadOnlySet<string> referencedLogicalPaths,
        DateTimeOffset cutoffUtc)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return 0;
        }

        var deleted = 0;
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var fullPath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(fullPath);
                if (info.LastWriteTimeUtc >= cutoffUtc)
                {
                    continue;
                }

                var logical = ToLogicalPath(normalizedRoot, fullPath);
                if (referencedLogicalPaths.Contains(logical))
                {
                    continue;
                }

                File.Delete(fullPath);
                deleted++;
            }
            catch (IOException)
            {
                // A concurrent delete race or a transient lock — skip this
                // file; the next sweep tick will retry if still stale.
            }
            catch (UnauthorizedAccessException)
            {
                // File permissions under the root may differ across
                // tenants; skip rather than crash the sweep.
            }
        }

        return deleted;
    }

    /// <summary>Maps an absolute <paramref name="fullPath"/> to its
    /// logical storage-path form (forward slashes, no root prefix).</summary>
    public static string ToLogicalPath(string normalizedRoot, string fullPath)
    {
        var relative = Path.GetRelativePath(normalizedRoot, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
