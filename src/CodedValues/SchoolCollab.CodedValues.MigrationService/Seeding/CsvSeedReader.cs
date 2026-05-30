using System.Text;

namespace SchoolCollab.CodedValues.MigrationService.Seeding;

/// <summary>
/// Reads coded value seed rows from a CSV file.
/// Format: Code,Name,Description,ParentCode,DisplayOrder
/// Supports quoted fields (RFC 4180). Codes are normalised to UPPER CASE.
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
