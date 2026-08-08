using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Tenancy;
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
    ITenantContextAccessor tenantContextAccessor,
    ILogger<CodedValueSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        // The seeder runs under the default (Guid.Empty) context and writes NULL-
        // blueprint CodedValue rows (hybrid guard allows null). Suppress the guard
        // for the whole pass as the sanctioned seed bypass (global-tenant-filter.md
        // §12 Step 5) so the writes are never rejected regardless of caller context.
        using (tenantContextAccessor.SuppressTenantGuard())
        {
            await SeedCodedValuesAsync(ct);
            await SeedAttributeDefinitionsAsync(ct);
            await SeedAttributeValuesAsync(ct);
        }
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
            // is already known (in DB or just inserted in this session). CodedValue.Create
            // assigns a client-generated Guid, so the parent→child link is resolved
            // in-memory and the WHOLE pass is persisted with a single SaveChangesAsync —
            // one round trip per tree-depth level instead of one per row. Fails fast if a
            // cycle or missing parent is detected.
            while (pending.Count > 0)
            {
                var ready = pending
                    .Where(r => string.IsNullOrEmpty(r.ParentCode) || codeToId.ContainsKey(r.ParentCode))
                    .ToList();

                if (ready.Count == 0)
                    throw new InvalidOperationException(
                        $"Seeding stalled — cycle detected or parent code does not exist in the " +
                        $"database or seed file. Remaining: {string.Join(", ", pending.Select(r => r.Code))}");

                foreach (var row in ready)
                {
                    var parentId = string.IsNullOrEmpty(row.ParentCode)
                        ? (Guid?)null
                        : codeToId[row.ParentCode];

                    var entity = CodedValue.Create(row.Code, row.Name, row.Description, parentId, row.DisplayOrder);
                    entity.ClearDomainEvents(); // not dispatching events for seed data
                    db.CodedValues.Add(entity);

                    codeToId[row.Code] = entity.Id;
                    pending.Remove(row);
                    inserted++;

                    logger.LogDebug("Seeded {Code} ({Name})", row.Code, row.Name);
                }

                await db.SaveChangesAsync(ct);
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

        // Pre-load all coded values (tracked) by code once so each definition row
        // resolves its parent in-memory instead of a round trip per row.
        var byCode = await db.CodedValues
            .Include(c => c.AttributeDefinitions)
            .ToDictionaryAsync(c => c.Code, ct);

        foreach (var row in rows)
        {
            if (!byCode.TryGetValue(row.ParentCode, out var parent))
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

        // Pre-load ALL coded values (tracked, with definitions) in ONE query so every
        // attribute row resolves its target in-memory instead of a round trip per row.
        // Attributes are auto-included (see CodedValueConfiguration) so the idempotency
        // check below is also in-memory. Two dictionaries: by code (for the target +
        // CodedValue-typed reference resolution) and by id (for the parent lookup).
        var entities = await db.CodedValues
            .Include(c => c.AttributeDefinitions)
            .ToListAsync(ct);
        var byCode = entities.ToDictionary(c => c.Code);
        var byId = entities.ToDictionary(c => c.Id);

        foreach (var row in rows)
        {
            if (!byCode.TryGetValue(row.Code, out var codedValue))
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

            var valueToStore = row.Value;

            if (codedValue.ParentId is { } parentId &&
                byId.TryGetValue(parentId, out var parent))
            {
                var definition = parent.AttributeDefinitions
                    .FirstOrDefault(d => d.Key == row.Key);

                if (definition?.DataType == AttributeDataType.CodedValue)
                {
                    if (!byCode.TryGetValue(row.Value, out var referenced))
                    {
                        logger.LogWarning(
                            "Attribute {Key} on {Code} is CodedValue-typed but referenced code {ReferencedCode} was not found. Skipping",
                            row.Key, row.Code, row.Value);
                        continue;
                    }

                    valueToStore = referenced.Id.ToString();
                    logger.LogDebug(
                        "Resolved CodedValue attribute {Key} on {Code}: {ReferencedCode} -> {ReferencedId}",
                        row.Key, row.Code, row.Value, referenced.Id);
                }
            }

            codedValue.SetAttribute(row.Key, valueToStore);

            logger.LogDebug("Added attribute {Key}={Value} on {Code}",
                row.Key, valueToStore, row.Code);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Attribute values seeding complete");
    }
}