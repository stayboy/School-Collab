using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Seeds coded values, attribute definitions, and attribute values from CSV files into the database.
/// Idempotent: skips rows whose Code already exists (for coded values) or whose
/// key already exists on the target entity (for definitions and attributes).
/// Handles arbitrary parent/child depth via iterative topological insertion.
/// File paths are read from Seeding:FilePath, Seeding:AttributeDefinitionsFilePath,
/// and Seeding:AttributeValuesFilePath config keys, falling back to seed.csv,
/// seed-attribute-definitions.csv, and seed-attributes.csv in the SeedData subdirectory.
/// </summary>
public sealed class CodedValueSeeder(
    SettingsDbContext db,
    IConfiguration configuration,
    ILogger<CodedValueSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedCodedValuesAsync(ct);
        await SeedAttributeDefinitionsAsync(ct);
        await SeedAttributeValuesAsync(ct);
    }

    private async Task SeedCodedValuesAsync(CancellationToken ct)
    {
        var seedFilePath = configuration["Seeding:FilePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "SeedData", "seed.csv");

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

    private async Task SeedAttributeDefinitionsAsync(CancellationToken ct)
    {
        var filePath = configuration["Seeding:AttributeDefinitionsFilePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "SeedData", "seed-attribute-definitions.csv");

        if (!File.Exists(filePath))
        {
            logger.LogDebug("Attribute definitions seed file not found at {Path}. Skipping", filePath);
            return;
        }

        logger.LogInformation("Reading attribute definitions seed file {Path}", filePath);
        var rows = CsvSeedReader.ReadAttributeDefinitions(filePath);
        logger.LogDebug("Read {Count} attribute definition rows", rows.Count);

        foreach (var row in rows)
        {
            var parent = await db.CodedValues
                .FirstOrDefaultAsync(c => c.Code == row.ParentCode, ct);

            if (parent is null)
            {
                logger.LogWarning("Parent coded value {Code} not found for attribute definition {Key}. Skipping",
                    row.ParentCode, row.Key);
                continue;
            }

            // Idempotent: skip if definition already exists
            if (parent.AttributeDefinitions.Any(d => d.Key == row.Key))
            {
                logger.LogDebug("Attribute definition {Key} already exists on {Code}. Skipping",
                    row.Key, row.ParentCode);
                continue;
            }

            parent.SetAttributeDefinition(
                row.Key, row.DataType, row.SourceCode, row.IsRequired, row.AllowMultiple,
                row.DisplayName, row.MinLength, row.MaxLength, row.RegexPattern);

            logger.LogDebug("Added attribute definition {Key} ({DataType}) on {Code}",
                row.Key, row.DataType, row.ParentCode);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Attribute definitions seeding complete");
    }

    private async Task SeedAttributeValuesAsync(CancellationToken ct)
    {
        var filePath = configuration["Seeding:AttributeValuesFilePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "SeedData", "seed-attributes.csv");

        if (!File.Exists(filePath))
        {
            logger.LogDebug("Attribute values seed file not found at {Path}. Skipping", filePath);
            return;
        }

        logger.LogInformation("Reading attribute values seed file {Path}", filePath);
        var rows = CsvSeedReader.ReadAttributes(filePath);
        logger.LogDebug("Read {Count} attribute value rows", rows.Count);

        foreach (var row in rows)
        {
            var codedValue = await db.CodedValues
                .FirstOrDefaultAsync(c => c.Code == row.Code, ct);

            if (codedValue is null)
            {
                logger.LogWarning("Coded value {Code} not found for attribute {Key}. Skipping",
                    row.Code, row.Key);
                continue;
            }

            // Idempotent: skip if attribute already exists
            if (codedValue.Attributes.Any(a => a.Key == row.Key))
            {
                logger.LogDebug("Attribute {Key} already exists on {Code}. Skipping",
                    row.Key, row.Code);
                continue;
            }

            codedValue.SetAttribute(row.Key, row.Value);

            logger.LogDebug("Added attribute {Key}={Value} on {Code}",
                row.Key, row.Value, row.Code);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Attribute values seeding complete");
    }
}