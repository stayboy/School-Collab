using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.MigrationService.Seeding;

/// <summary>
/// Seeds coded values from a CSV file into the database.
/// Idempotent: skips rows whose Code already exists.
/// Handles arbitrary parent/child depth via iterative topological insertion.
/// File path is read from Seeding:FilePath config key, falling back to seed.csv
/// next to the executable.
/// </summary>
public sealed class CodedValueSeeder(
    CodedValuesDbContext db,
    IConfiguration configuration,
    ILogger<CodedValueSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var seedFilePath = configuration["Seeding:FilePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "seed.csv");

        if (!File.Exists(seedFilePath))
        {
            logger.LogWarning("Seed file not found at {Path}. Skipping seeding", seedFilePath);
            return;
        }

        logger.LogInformation("Reading seed file {Path}", seedFilePath);
        var rows = CsvSeedReader.Read(seedFilePath);
        logger.LogDebug("Read {Count} rows from seed file", rows.Count);

        // Load all existing code → id mappings once to avoid N+1 lookups
        var codeToId = await db.CodedValues
            .ToDictionaryAsync(x => x.Code, x => x.Id, ct);

        // Only insert rows not already present in the database
        var pending = rows.Where(r => !codeToId.ContainsKey(r.Code)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("All seed rows already exist in the database. Nothing to insert");
            return;
        }

        logger.LogInformation("Inserting {Count} new coded values", pending.Count);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var inserted = 0;

            // Iterative topological insertion: each pass inserts rows whose parent
            // is already known (in DB or just inserted in this session). Handles
            // arbitrary tree depth. Fails fast if a cycle or missing parent is detected.
            while (pending.Count > 0)
            {
                var progress = false;

                foreach (var row in pending.ToList())
                {
                    if (!string.IsNullOrEmpty(row.ParentCode) && !codeToId.ContainsKey(row.ParentCode))
                        continue; // parent not yet inserted — defer to next pass

                    var parentId = string.IsNullOrEmpty(row.ParentCode)
                        ? (Guid?)null
                        : codeToId[row.ParentCode];

                    var entity = CodedValue.Create(row.Code, row.Name, row.Description, parentId, row.DisplayOrder);
                    db.CodedValues.Add(entity);
                    await db.SaveChangesAsync(ct);
                    entity.ClearDomainEvents(); // not dispatching events for seed data

                    codeToId[row.Code] = entity.Id;
                    pending.Remove(row);
                    inserted++;
                    progress = true;

                    logger.LogDebug("Seeded {Code} ({Name})", row.Code, row.Name);
                }

                if (!progress)
                    throw new InvalidOperationException(
                        $"Seeding stalled — cycle detected or parent code does not exist in the " +
                        $"database or seed file. Remaining: {string.Join(", ", pending.Select(r => r.Code))}");
            }

            await tx.CommitAsync(ct);
            logger.LogInformation("Seeded {Count} coded values successfully", inserted);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
