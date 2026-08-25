using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Worker;

/// <summary>
/// One-time startup hydration of the local coded-value read model from
/// settings-api — the single sync reference-data hop permitted by
/// adr-cross-module-calls.md, and never on a user-facing write path.
///
/// <para><b>Gating:</b> runs only while <c>Students:UseLocalCodedValueProjection</c>
/// is <b>off</b> — per adr-cross-module-calls.md Phase 1 step 4/6, the table warms
/// behind the flag (consumer + backfill populating, reads still via HTTP) so that
/// flipping the flag finds a complete local read model. Once the flag is on, the
/// consumer maintains the table and the backfill stops.</para>
///
/// <para><b>Strategy:</b> walks the coded-value tree breadth-first from the root
/// list (<c>GET /api/coded-values/</c>, then <c>/by-parent?...&amp;includeDisabled=true</code>
/// per node), upserting each value as a GLOBAL row (TenantId = null). Runs under
/// no tenant context, so settings-api resolves its shared blueprint — exactly the
/// global scope the projection needs. Upserts are guarded by <c>UpdatedAt</c> so a
/// stale snapshot can never overwrite newer event-sourced rows that arrived while
/// the backfill was running.</para>
///
/// <para><b>Tenant overrides are NOT backfilled</b>: settings-api exposes no
/// cross-tenant override enumeration, and overrides only change display names.
/// They reach the projection through <c>CodedValueOverrideUpserted</c> events as
/// tenants re-save them; legacy overrides keep global names until then (an accepted
/// display-only gap — enroll validation reads global attributes, which ARE
/// backfilled).</para>
///
/// <para><b>Failure policy:</b> failures are logged and swallowed — a downed
/// settings-api at startup must not crash the worker. The next restart retries;
/// events heal the gap meanwhile.</para>
/// </summary>
public sealed class CodedValueBackfillService(
    IServiceProvider provider,
    IConfiguration configuration,
    ILogger<CodedValueBackfillService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record BackfillDto(
        Guid Id,
        string Code,
        string Name,
        string? Description,
        Guid? ParentId,
        string? ParentCode,
        bool IsDisabled,
        int DisplayOrder,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyCollection<BackfillAttribute>? Attributes,
        bool? IsDeleted);
    private sealed record BackfillAttribute(string Key, string Value);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (configuration.GetValue("Students:UseLocalCodedValueProjection", defaultValue: false))
        {
            logger.LogInformation("CodedValueBackfill skipped: Students:UseLocalCodedValueProjection is on (table already warmed)");
            return;
        }

        try
        {
            await RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown — nothing to do
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CodedValueBackfill failed; projection will rely on events until next restart");
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("settings-api");
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<StudentsDbContext>>();

        int total = 0;
        var queue = new Queue<Guid?>();

        // Roots first (null parent), then BFS via by-parent.
        var roots = await http.GetFromJsonAsync<BackfillDto[]>("/api/coded-values/", JsonOptions, ct)
                    ?? [];
        foreach (var root in roots)
        {
            await UpsertAsync(dbFactory, root, ct);
            queue.Enqueue(root.Id);
            total++;
        }
        logger.LogInformation("CodedValueBackfill seeded {Count} root coded values", roots.Length);

        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            var children = await http.GetFromJsonAsync<BackfillDto[]>(
                $"/api/coded-values/by-parent?parentId={parentId}&includeDisabled=true", JsonOptions, ct)
                ?? [];

            foreach (var child in children)
            {
                await UpsertAsync(dbFactory, child, ct);
                queue.Enqueue(child.Id);
                total++;
            }
        }

        logger.LogInformation("CodedValueBackfill complete: {Total} coded values hydrated into local_coded_values", total);
    }

    /// <summary>UpdatedAt-guarded upsert of a GLOBAL row (never overwrites newer event-sourced state).</summary>
    private static async Task UpsertAsync(
        IDbContextFactory<StudentsDbContext> dbFactory, BackfillDto dto, CancellationToken ct)
    {
        if (dto.IsDeleted == true)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.LocalCodedValues
            .SingleOrDefaultAsync(x => x.TenantId == null && x.Id == dto.Id, ct);

        if (existing is not null && existing.UpdatedAt > dto.UpdatedAt)
        {
            return; // event already wrote newer state
        }

        if (existing is null)
        {
            db.LocalCodedValues.Add(new LocalCodedValue
            {
                Id = dto.Id,
                TenantId = null,
                Code = dto.Code,
                Name = dto.Name,
                Description = dto.Description,
                ParentId = dto.ParentId,
                ParentCode = dto.ParentCode,
                IsDisabled = dto.IsDisabled,
                DisplayOrder = dto.DisplayOrder,
                Attributes = dto.Attributes?
                    .Select(a => new LocalCodedValueAttribute(a.Key, a.Value)).ToList() ?? [],
                CreatedAt = dto.CreatedAt,
                UpdatedAt = dto.UpdatedAt,
            });
        }
        else
        {
            existing.Code = dto.Code;
            existing.Name = dto.Name;
            existing.Description = dto.Description;
            existing.ParentId = dto.ParentId;
            existing.ParentCode = dto.ParentCode;
            existing.IsDisabled = dto.IsDisabled;
            existing.DisplayOrder = dto.DisplayOrder;
            existing.Attributes = dto.Attributes?
                .Select(a => new LocalCodedValueAttribute(a.Key, a.Value)).ToList() ?? [];
            existing.UpdatedAt = dto.UpdatedAt;
        }

        await db.SaveChangesAsync(ct);
    }
}
