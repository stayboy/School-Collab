using System.Text.RegularExpressions;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Helpers;

/// <summary>
/// The R3 deterministic prompt composition (spec §6.1a PB-4 — the normative skeleton). This is the
/// SINGLE predicate source shared by PB-5 (replace-on-confirm), PB-6 (re-hydration) and QA-23
/// (Generate-time refresh): the narrative is a fixed line set, and "unedited" means the text still
/// structurally matches it. There is no AI call and no transient "last composed" value anywhere —
/// the template match <em>is</em> the marker, which is what makes the rule behave identically
/// before and after a page reload.
/// </summary>
public static class QuestionPromptComposer
{
    /// <summary>The <c>Include:</c> value when every type chip is off — the server's balanced default.</summary>
    public const string BalancedMix = "balanced mix";

    /// <summary>Canonical type order; also the order names appear in the <c>Include:</c> line.</summary>
    public static readonly IReadOnlyList<QuestionTypeDto> AllTypes =
    [
        QuestionTypeDto.MultipleChoice,
        QuestionTypeDto.TrueFalse,
        QuestionTypeDto.ShortAnswer,
    ];

    private static readonly Regex SubjectLine =
        new("^Generate (?<n>\\d+) questions for the subject \"(?<topic>.*)\"\\.$", RegexOptions.Compiled);

    private static readonly Regex GradeLine =
        new("^Grade level: (?<grades>.+)\\.$", RegexOptions.Compiled);

    private static readonly Regex DifficultyLine =
        new("^Difficulty mix: (?<e>\\d+) easy / (?<m>\\d+) medium / (?<h>\\d+) hard\\.$", RegexOptions.Compiled);

    private static readonly Regex IncludeLine =
        new("^Include: (?<list>.+)\\.$", RegexOptions.Compiled);

    private static readonly Regex GroundedLine =
        new("^Grounded on: (?<names>.+)\\.$", RegexOptions.Compiled);

    private static readonly Regex StrandsLine =
        new("^Strands: (?<names>.+)\\.$", RegexOptions.Compiled);

    private static readonly Regex LessonsLine =
        new("^Lessons: (?<names>.+)\\.$", RegexOptions.Compiled);

    /// <summary>The one canonical, user-visible spelling for a question type.</summary>
    public static string TypeName(QuestionTypeDto type) => EnumHelper.GetDescription(type);

    /// <summary>
    /// <c>Grade 1, Grade 2</c>; <c>Grades 1–4</c> when the levels are contiguous and there are more
    /// than two; <c>—</c> when the assignment targets no grade (PB-3/PB-4 — the line is never omitted).
    /// </summary>
    public static string FormatGrades(IReadOnlyList<int>? levels)
    {
        var sorted = (levels ?? [])
            .Where(l => l > 0)
            .Distinct()
            .OrderBy(l => l)
            .ToList();

        return sorted.Count switch
        {
            0 => "—",
            > 2 when sorted[^1] - sorted[0] + 1 == sorted.Count => $"Grades {sorted[0]}–{sorted[^1]}",
            _ => string.Join(", ", sorted.Select(l => $"Grade {l}")),
        };
    }

    /// <summary>Canonical, de-duplicated, enum-ordered subset of <paramref name="types"/>.</summary>
    public static IReadOnlyList<QuestionTypeDto> NormalizeTypes(IReadOnlyList<QuestionTypeDto>? types)
    {
        var picked = types ?? [];
        return AllTypes.Where(picked.Contains).ToList();
    }

    /// <summary>The <c>Include:</c> value — canonical names, or <see cref="BalancedMix"/> when none.</summary>
    public static string FormatTypes(IReadOnlyList<QuestionTypeDto>? types)
    {
        var picked = NormalizeTypes(types);
        return picked.Count == 0 ? BalancedMix : string.Join(", ", picked.Select(TypeName));
    }

    /// <summary>The deterministic narrative (PB-4). Line order and labels are normative.</summary>
    public static string Compose(QuestionPromptNarrativeInputs inputs)
    {
        var lines = new List<string>
        {
            $"Generate {inputs.QuestionCount} questions for the subject \"{inputs.TopicName ?? string.Empty}\".",
            $"Grade level: {FormatGrades(inputs.GradeLevels)}.",
            $"Difficulty mix: {inputs.DifficultyEasy ?? 0} easy / {inputs.DifficultyMedium ?? 0} medium / {inputs.DifficultyHard ?? 0} hard.",
            $"Include: {FormatTypes(inputs.Types)}.",
        };

        AddOptional("Grounded on: ", inputs.ResourceNames);
        AddOptional("Strands: ", inputs.StrandNames);
        AddOptional("Lessons: ", inputs.LessonNames);

        // "\n" (never "\r\n") so the composed text is byte-identical across platforms and the
        // server round-trip cannot change it.
        return string.Join("\n", lines);

        void AddOptional(string prefix, IReadOnlyList<string>? names)
        {
            if (names is { Count: > 0 })
            {
                lines.Add($"{prefix}{string.Join(", ", names)}.");
            }
        }
    }

    /// <summary>The QA-3 read-only summary — the same parts as the narrative, rendered inline.
    /// R4 (CP-7): the two pick collections append their own <c> · Strands: …</c> /
    /// <c> · Lessons: …</c> segment when non-empty, in the same order the narrative renders
    /// them. The composer only renders what it is handed: the D19/G40 lock filtering (a locked
    /// org loses the lesson names) happens at the section, so this stays deterministic.</summary>
    public static string ComposeSummary(QuestionPromptNarrativeInputs inputs)
    {
        var difficulty = (inputs.DifficultyEasy, inputs.DifficultyMedium, inputs.DifficultyHard) is (null, null, null)
            ? "—"
            : $"{inputs.DifficultyEasy ?? 0} easy / {inputs.DifficultyMedium ?? 0} medium / {inputs.DifficultyHard ?? 0} hard";

        var picked = NormalizeTypes(inputs.Types);
        var types = picked.Count == 0 ? "Balanced mix" : string.Join(", ", picked.Select(TypeName));

        var summary = $"{inputs.QuestionCount} questions · {difficulty} · {types}";

        if (inputs.StrandNames is { Count: > 0 } strands)
        {
            summary += $" · Strands: {string.Join(", ", strands)}";
        }

        if (inputs.LessonNames is { Count: > 0 } lessons)
        {
            summary += $" · Lessons: {string.Join(", ", lessons)}";
        }

        return summary;
    }

    /// <summary>PB-5/PB-6/QA-23's shared "unedited" predicate — a COMPLETE structural match of PB-4.</summary>
    public static bool IsTemplateMatch(string? text) => TryParse(text, out _);

    /// <summary>
    /// Narrow deterministic parse (PB-6). Returns <c>false</c> unless the text matches the PB-4
    /// skeleton in full — every mandatory line, in order, with the optional lines in order and
    /// nothing left over. Hand-written prose never matches, so it is never reverse-engineered.
    /// </summary>
    public static bool TryParse(string? text, out QuestionPromptKnobs knobs)
    {
        knobs = default!;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var i = 0;

        // (1) subject — mandatory.
        if (i >= lines.Length)
        {
            return false;
        }

        var subject = SubjectLine.Match(lines[i++]);
        if (!subject.Success || !int.TryParse(subject.Groups["n"].Value, out var count))
        {
            return false;
        }

        // (2) grade — mandatory (renders "—" when unset).
        if (i >= lines.Length || !GradeLine.IsMatch(lines[i++]))
        {
            return false;
        }

        // (3) difficulty — mandatory (zeros rendered when unset).
        if (i >= lines.Length || !DifficultyLine.IsMatch(lines[i++]))
        {
            return false;
        }

        // (4) include — mandatory (renders "balanced mix" when all chips are off).
        if (i >= lines.Length)
        {
            return false;
        }

        var include = IncludeLine.Match(lines[i++]);
        if (!include.Success || !TryParseTypes(include.Groups["list"].Value, out var types))
        {
            return false;
        }

        // (5-7) R3's resource line, then R4's two pick lines — each optional, strict order.
        if (i < lines.Length && GroundedLine.IsMatch(lines[i]))
        {
            i++;
        }

        if (i < lines.Length && StrandsLine.IsMatch(lines[i]))
        {
            i++;
        }

        if (i < lines.Length && LessonsLine.IsMatch(lines[i]))
        {
            i++;
        }

        if (i != lines.Length)
        {
            return false;
        }

        knobs = new QuestionPromptKnobs(count, types);
        return true;
    }

    private static bool TryParseTypes(string list, out IReadOnlyList<QuestionTypeDto> types)
    {
        types = [];

        if (list == BalancedMix)
        {
            return true;
        }

        var names = list.Split(", ");
        var picked = new List<QuestionTypeDto>(names.Length);

        foreach (var name in names)
        {
            var matches = AllTypes.Where(t => TypeName(t) == name).ToList();
            if (matches.Count != 1 || picked.Contains(matches[0]))
            {
                return false;
            }

            picked.Add(matches[0]);
        }

        // The names must be the canonical order (no "Short answer, Multiple choice").
        if (!picked.SequenceEqual(NormalizeTypes(picked)))
        {
            return false;
        }

        types = picked;
        return true;
    }
}

/// <summary>The inputs a narrative is composed from (PB-4).</summary>
/// <param name="TopicName">The selected subject's display name.</param>
/// <param name="GradeLevels">The targeted grades' canonical <c>GradeLevel.Level</c> values.</param>
/// <param name="QuestionCount">The count knob.</param>
/// <param name="DifficultyEasy">Easy count; null reads as 0.</param>
/// <param name="DifficultyMedium">Medium count; null reads as 0.</param>
/// <param name="DifficultyHard">Hard count; null reads as 0.</param>
/// <param name="Types">Selected types; empty/null = the balanced mix.</param>
/// <param name="ResourceNames">Attached file/link names; empty = no <c>Grounded on:</c> line.</param>
/// <param name="StrandNames">R4 pick names; empty in R3.</param>
/// <param name="LessonNames">R4 pick names; empty in R3.</param>
public sealed record QuestionPromptNarrativeInputs(
    string? TopicName,
    IReadOnlyList<int>? GradeLevels,
    int QuestionCount,
    int? DifficultyEasy,
    int? DifficultyMedium,
    int? DifficultyHard,
    IReadOnlyList<QuestionTypeDto>? Types,
    IReadOnlyList<string>? ResourceNames,
    IReadOnlyList<string>? StrandNames = null,
    IReadOnlyList<string>? LessonNames = null);

/// <summary>What the narrow parse recovers from a template-matched prompt (PB-6).</summary>
public sealed record QuestionPromptKnobs(int QuestionCount, IReadOnlyList<QuestionTypeDto> Types);
