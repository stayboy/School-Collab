using System.Globalization;
using System.Text;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.MigrationService.Seeding;

/// <summary>
/// Reads seed rows from CSV files for coded values, attribute definitions, and attribute values.
/// All formats support quoted fields (RFC 4180). Codes are normalised to UPPER CASE.
/// </summary>
public static class CsvSeedReader
{
    private const int MaxCodeLength = 100;
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 500;

    public static IReadOnlyList<CodedValueSeedRow> Read(string filePath)
    {
        var lines = File.ReadAllLines(filePath); // UTF-8 with BOM detection
        var rows = new List<CodedValueSeedRow>(lines.Length);

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++) // skip header row
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = ParseLine(line);
            if (fields.Length < 5)
                throw new FormatException(
                    $"Line {lineIndex + 1}: expected 5 fields (Code,Name,Description,ParentCode,DisplayOrder) but got {fields.Length}.");

            var code = fields[0].Trim().ToUpperInvariant();
            var name = fields[1].Trim();
            var description = string.IsNullOrWhiteSpace(fields[2]) ? null : fields[2].Trim();
            var parentCode = string.IsNullOrWhiteSpace(fields[3]) ? null : fields[3].Trim().ToUpperInvariant();
            var displayOrder = int.TryParse(fields[4].Trim(), out var order) ? order : 0;

            ValidateField(lineIndex + 1, "Code", code, MaxCodeLength, required: true);
            ValidateField(lineIndex + 1, "Name", name, MaxNameLength, required: true);
            ValidateField(lineIndex + 1, "Description", description, MaxDescriptionLength, required: false);

            rows.Add(new CodedValueSeedRow(code, name, description, parentCode, displayOrder));
        }

        // Catch duplicate codes within the file itself
        var duplicates = rows
            .GroupBy(r => r.Code)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate codes in seed file: {string.Join(", ", duplicates)}");

        return rows;
    }

    /// <summary>
    /// Reads attribute definition seed rows from a CSV file.
    /// Format: ParentCode,Key,DisplayName,DataType,SourceCode,IsRequired,AllowMultiple,MinLength,MaxLength,RegexPattern
    /// DataType values: Text=0, Integer=1, Decimal=2, Boolean=3, Date=4, DateTime=5, Time=6, CodedValue=7
    /// </summary>
    public static IReadOnlyList<AttributeDefinitionSeedRow> ReadAttributeDefinitions(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var rows = new List<AttributeDefinitionSeedRow>(lines.Length);

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = ParseLine(line);
            if (fields.Length < 10)
                throw new FormatException(
                    $"Line {lineIndex + 1}: expected 10 fields (ParentCode,Key,DisplayName,DataType,SourceCode,IsRequired,AllowMultiple,MinLength,MaxLength,RegexPattern) but got {fields.Length}.");

            var parentCode = fields[0].Trim().ToUpperInvariant();
            var key = fields[1].Trim();
            var displayName = string.IsNullOrWhiteSpace(fields[2]) ? null : fields[2].Trim();
            var dataType = (AttributeDataType)int.Parse(fields[3].Trim(), CultureInfo.InvariantCulture);
            var sourceCode = string.IsNullOrWhiteSpace(fields[4]) ? null : fields[4].Trim().ToUpperInvariant();
            var isRequired = bool.TryParse(fields[5].Trim(), out var req) && req;
            var allowMultiple = bool.TryParse(fields[6].Trim(), out var multi) && multi;
            var minLength = string.IsNullOrWhiteSpace(fields[7]) ? (int?)null : int.Parse(fields[7].Trim(), CultureInfo.InvariantCulture);
            var maxLength = string.IsNullOrWhiteSpace(fields[8]) ? (int?)null : int.Parse(fields[8].Trim(), CultureInfo.InvariantCulture);
            var regexPattern = string.IsNullOrWhiteSpace(fields[9]) ? null : fields[9].Trim();

            ValidateField(lineIndex + 1, "ParentCode", parentCode, MaxCodeLength, required: true);
            ValidateField(lineIndex + 1, "Key", key, MaxCodeLength, required: true);

            rows.Add(new AttributeDefinitionSeedRow(parentCode, key, displayName, dataType, sourceCode,
                isRequired, allowMultiple, minLength, maxLength, regexPattern));
        }

        return rows;
    }

    /// <summary>
    /// Reads attribute value seed rows from a CSV file.
    /// Format: Code,Key,Value
    /// Code is the coded value code (child), Key is the attribute key, Value is the attribute value.
    /// </summary>
    public static IReadOnlyList<AttributeSeedRow> ReadAttributes(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var rows = new List<AttributeSeedRow>(lines.Length);

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = ParseLine(line);
            if (fields.Length < 3)
                throw new FormatException(
                    $"Line {lineIndex + 1}: expected 3 fields (Code,Key,Value) but got {fields.Length}.");

            var code = fields[0].Trim().ToUpperInvariant();
            var key = fields[1].Trim();
            var value = fields[2].Trim();

            ValidateField(lineIndex + 1, "Code", code, MaxCodeLength, required: true);
            ValidateField(lineIndex + 1, "Key", key, MaxCodeLength, required: true);

            rows.Add(new AttributeSeedRow(code, key, value));
        }

        return rows;
    }

    private static void ValidateField(int lineNumber, string fieldName, string? value, int maxLength, bool required)
    {
        if (required && string.IsNullOrEmpty(value))
            throw new FormatException($"Line {lineNumber}: {fieldName} cannot be empty.");

        if (value is not null && value.Length > maxLength)
            throw new FormatException(
                $"Line {lineNumber}: {fieldName} '{value[..Math.Min(value.Length, 30)]}…' exceeds max length of {maxLength}.");
    }

    private static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"'); // escaped quote ""
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return [.. fields];
    }
}