using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Local-FS dev implementation of <see cref="IFileStore"/> (WS-A1 / D-1).
/// Persists files under <see cref="AssignmentFileStoreOptions.RootPath"/>,
/// tenant-scoped at <c>tenants/{tenantId}/staging/{guid}/{safeName}</c>.
/// The Azure Blob port deferred for later is expected to share the same
/// opaque-path contract so the entity <c>StoragePath</c> values stay
/// round-trippable across deployments.
/// </summary>
public sealed class LocalFileStore(
    ITenantProvider tenantProvider,
    IOptions<AssignmentFileStoreOptions> storeOptions,
    ILogger<LocalFileStore> logger) : IFileStore
{
    private readonly string _root = ResolveRoot(storeOptions.Value.RootPath);

    public async Task<string> StoreAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));

        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new ArgumentException(
                "File name does not contain a usable base name.", nameof(fileName));
        }

        var relative = $"tenants/{tenantId:N}/staging/{Guid.NewGuid():N}/{safeName}";
        var fullPath = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "Resolved staging path has no directory component.");
        Directory.CreateDirectory(directory);

        await using (var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        logger.LogInformation(
            "Stored staging file {FileName} ({Size} bytes) for tenant {TenantId} at {StoragePath}",
            safeName, new FileInfo(fullPath).Length, tenantId, relative);
        return relative;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveWithinRoot(storagePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Staged file not found: {storagePath}", fullPath);
        }
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        // Idempotent: a missing file is a no-op (decision (b) sweep + general
        // robustness against double-delete races).
        var fullPath = ResolveWithinRoot(storagePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            logger.LogDebug("Deleted staged file {StoragePath}", storagePath);
        }
        return Task.CompletedTask;
    }

    /// <summary>Maps a logical <paramref name="storagePath"/> to a full
    /// path on disk, throwing when the path is rooted, contains <c>..</c>
    /// segments, or resolves outside the configured root (traversal
    /// guard). Public so the orphan sweep can reuse the same mapping.</summary>
    public string ResolveWithinRoot(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            throw new ArgumentException("Storage path is required.", nameof(storagePath));
        if (Path.IsPathRooted(storagePath))
        {
            throw new ArgumentException(
                $"Storage path '{storagePath}' must not be rooted.", nameof(storagePath));
        }
        if (storagePath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Storage path '{storagePath}' must not contain '..' segments.", nameof(storagePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(
            _root, storagePath.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(_root)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Storage path '{storagePath}' resolves outside the file-store root.", nameof(storagePath));
        }
        return fullPath;
    }

    private static string ResolveRoot(string configuredRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot))
            throw new InvalidOperationException(
                "Assignments:FileStore:RootPath must be configured.");
        if (Path.IsPathRooted(configuredRoot))
            return Path.GetFullPath(configuredRoot);
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredRoot));
    }
}
