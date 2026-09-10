using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// Orphan-sweep BackgroundService for staged uploads (WS-A1 / decision
/// (b)). Runs every <see cref="AttachmentUploadOptions.SweepIntervalHours"/>
/// hours and deletes staged files older than
/// <see cref="AttachmentUploadOptions.StagingRetentionHours"/> that are NOT
/// referenced by any assignment attachment, resource, or content-module
/// row (decision (b) — the reference check is the correctness core; a
/// missing check would delete the very blobs the created assignment
/// points at). Uses <c>IgnoreQueryFilters(["Tenant"])</c> for a read-only
/// cross-tenant projection (the sanctioned opt-out the tenancy tests
/// already use; NO tenant-entity writes happen in the sweep).
/// </summary>
public sealed class StagedFileSweepService(
    IServiceScopeFactory scopeFactory,
    IOptions<AttachmentUploadOptions> uploadOptions,
    IOptions<AssignmentFileStoreOptions> storeOptions,
    ILogger<StagedFileSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, uploadOptions.Value.SweepIntervalHours));
        logger.LogInformation(
            "Staged-file sweep starting — interval {IntervalHours}h, retention {RetentionHours}h",
            interval.TotalHours, uploadOptions.Value.StagingRetentionHours);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A failed sweep must not crash the host; the next tick retries.
                logger.LogError(ex, "Staged-file sweep failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break; // shutting down
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssignmentsDbContext>();

        // Read-only cross-tenant projection: collect every storage_path the
        // created assignments currently reference so the sweep never deletes
        // a blob a live row points at (decision (b) reference check).
        // IgnoreQueryFilters(["Tenant"]) is the sanctioned opt-out the
        // tenancy tests already exercise — the sweep performs NO writes
        // here, so the tenant guard is irrelevant.
        var attachmentPaths = await db.Assignments
            .IgnoreQueryFilters(["Tenant"])
            .SelectMany(a => a.Attachments)
            .Select(x => x.StoragePath)
            .ToListAsync(cancellationToken);
        var resourcePaths = await db.AssignmentResources
            .IgnoreQueryFilters(["Tenant"])
            .Where(r => r.StoragePath != null)
            .Select(r => r.StoragePath!)
            .ToListAsync(cancellationToken);
        var modulePaths = await db.ContentModules
            .IgnoreQueryFilters(["Tenant"])
            .Where(m => m.StoragePath != null)
            .Select(m => m.StoragePath!)
            .ToListAsync(cancellationToken);

        var referenced = new HashSet<string>(
            attachmentPaths.Count + resourcePaths.Count + modulePaths.Count,
            StringComparer.OrdinalIgnoreCase);
        foreach (var p in attachmentPaths) referenced.Add(p);
        foreach (var p in resourcePaths) referenced.Add(p);
        foreach (var p in modulePaths) referenced.Add(p);

        var root = ResolveRoot(storeOptions.Value.RootPath);
        var cutoff = DateTimeOffset.UtcNow
            - TimeSpan.FromHours(Math.Max(1, uploadOptions.Value.StagingRetentionHours));

        var deleted = StagedFileSweeper.DeleteStaleFiles(root, referenced, cutoff);
        logger.LogInformation(
            "Staged-file sweep: scanned root {Root}, retained {ReferencedCount} referenced paths, deleted {DeletedCount} stale files",
            root, referenced.Count, deleted);
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
