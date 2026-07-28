using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// One ordered piece of an <see cref="EntityCodeRule"/> template. The final
/// generated code is the concatenation of all segments ordered by <see cref="Index"/>
/// (spec §3.2).
/// <para>
/// <b>Per-segment sequence state</b> (<see cref="LastSequence"/>,
/// <see cref="LastPrefix"/>, <see cref="LastPeriodBucket"/>) is persisted on the
/// segment so different segments on the same rule can reset on different schedules
/// (spec §3.3).
/// </para>
/// <para>
/// <b><c>Prefix</c> semantics differ by <see cref="SegmentType"/>:</b>
/// <list type="bullet">
/// <item><see cref="SegmentType.NumericSequence"/> — static leading text rendered
///   before the number (e.g. <c>""</c> or <c>"STU-"</c>).</item>
/// <item><see cref="SegmentType.AlphabeticSequence"/> / <see cref="SegmentType.AlphanumericSequence"/>
///   — the <b>initial alphabetic prefix</b> (e.g. <c>"A"</c>); used to reset
///   <see cref="LastPrefix"/> on a period change. The rendered output uses
///   <see cref="LastPrefix"/> (the current prefix), not <c>Prefix</c> directly.</item>
/// </list>
/// </para>
/// </summary>
public sealed class EntityCodeSegment
{
    // EF Core ctor
    private EntityCodeSegment() { }

    public Guid Id { get; private set; }
    public Guid EntityCodeRuleId { get; private set; }
    public int Index { get; private set; }
    public string? Role { get; private set; }
    public SegmentType Type { get; private set; }
    public string FixedText { get; private set; } = "";
    public string Prefix { get; private set; } = "";
    public string Suffix { get; private set; } = "";
    public ResetPeriod ResetPeriod { get; private set; }
    public int MinWidth { get; private set; }
    public string? UpperLimit { get; private set; }

    // ── Runtime sequence state (mutated by Advance) ──
    public int LastSequence { get; private set; }
    public string? LastPrefix { get; private set; }
    public string? LastPeriodBucket { get; private set; }

    /// <summary>Factory for a Fixed segment.</summary>
    public static EntityCodeSegment Fixed(
        int index, string? role, string fixedText, string suffix = "")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixedText);

        return new EntityCodeSegment
        {
            Id = Guid.NewGuid(),
            Index = index,
            Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim(),
            Type = SegmentType.Fixed,
            FixedText = fixedText,
            Suffix = suffix ?? "",
            MinWidth = 0
        };
    }

    /// <summary>Factory for a sequence segment (numeric / alphabetic / alphanumeric).</summary>
    public static EntityCodeSegment Sequence(
        int index,
        string? role,
        SegmentType type,
        string prefix = "",
        string suffix = "",
        ResetPeriod resetPeriod = ResetPeriod.None,
        int minWidth = 2,
        string? upperLimit = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (type == SegmentType.Fixed)
            throw new ArgumentException("Use Fixed() for a Fixed segment.", nameof(type));
        if (minWidth < 1)
            throw new ArgumentOutOfRangeException(nameof(minWidth), minWidth, "MinWidth must be >= 1.");

        return new EntityCodeSegment
        {
            Id = Guid.NewGuid(),
            Index = index,
            Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim(),
            Type = type,
            Prefix = prefix ?? "",
            Suffix = suffix ?? "",
            ResetPeriod = resetPeriod,
            MinWidth = minWidth,
            UpperLimit = string.IsNullOrWhiteSpace(upperLimit) ? null : upperLimit.Trim()
        };
    }

    /// <summary>
    /// Advances this segment's sequence state for the period containing
    /// <paramref name="now"/> and returns the rendered text for this segment.
    /// <see cref="SegmentType.Fixed"/> segments are unchanged.
    /// </summary>
    public string Advance(DateTimeOffset now)
    {
        if (Type == SegmentType.Fixed)
            return FixedText + Suffix;

        var bucket = ResetPeriod.ComputeBucket(now);
        if (bucket != LastPeriodBucket)
        {
            LastPeriodBucket = bucket;
            LastSequence = 0;
            // Reset the alphabetic prefix to its pre-first state:
            //  - AlphabeticSequence: "" (pre-A) so the first Advance yields "A".
            //  - AlphanumericSequence: the initial letter (Prefix, default "A");
            //    the letter does NOT increment on the first Advance (only the number does).
            LastPrefix = Type switch
            {
                SegmentType.AlphabeticSequence => "",
                SegmentType.AlphanumericSequence => string.IsNullOrWhiteSpace(Prefix) ? "A" : Prefix,
                _ => null
            };
        }

        switch (Type)
        {
            case SegmentType.NumericSequence:
                LastSequence++;
                if (LastSequence > MaxNumericValue)
                    throw new EntityCodeGenerationCollisionException(Describe());
                break;

            case SegmentType.AlphabeticSequence:
                LastPrefix = NextAlpha(string.IsNullOrEmpty(LastPrefix) ? "" : LastPrefix!);
                if (CompareAlpha(LastPrefix!, MaxAlphaValue) > 0)
                    throw new EntityCodeGenerationCollisionException(Describe());
                break;

            case SegmentType.AlphanumericSequence:
                LastSequence++;
                if (LastSequence > MaxNumericValue)
                {
                    // Rollover: increment the alphabetic prefix and reset the number to 1.
                    LastPrefix = NextAlpha(LastPrefix ?? (string.IsNullOrWhiteSpace(Prefix) ? "A" : Prefix));
                    LastSequence = 1;
                    if (CompareAlpha(LastPrefix!, MaxAlphaValue) > 0)
                        throw new EntityCodeGenerationCollisionException(Describe());
                }
                break;
        }

        return Render();
    }

    /// <summary>Renders the current state of this segment without advancing.</summary>
    public string Render() => Type switch
    {
        SegmentType.Fixed => FixedText + Suffix,
        SegmentType.NumericSequence => Prefix + LastSequence.ToString($"D{MinWidth}") + Suffix,
        SegmentType.AlphabeticSequence => Prefix + (LastPrefix ?? "") + Suffix,
        SegmentType.AlphanumericSequence => (LastPrefix ?? Prefix ?? "A") + LastSequence.ToString($"D{MinWidth}") + Suffix,
        _ => string.Empty
    };

    /// <summary>
    /// Renders the segment using the current (mutated) sequence state, with
    /// the supplied per-field FORMAT overrides applied transiently. Used by
    /// <see cref="EntityCodeRule.GenerateNextWithOverrides"/> so a
    /// tenant's override renders a different format WITHOUT mutating the
    /// persisted segment's fields — only the shared sequence state
    /// (mutated by <see cref="Advance"/>) is persisted.
    /// <para>
    /// If <paramref name="overrides"/> is null or empty this is equivalent
    /// to <see cref="Render"/>. Numeric overrides (ResetPeriod, MinWidth)
    /// are NOT applied here because they need to take effect at Advance
    /// time (period bucket calc, sequence width) — the generator path
    /// enforces those server-side.
    /// </para>
    /// </summary>
    public string RenderWithOverrides(IReadOnlyDictionary<OverrideField, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
            return Render();

        var fixedText = overrides.TryGetValue(OverrideField.FixedText, out var ft) && !string.IsNullOrEmpty(ft)
            ? ft
            : FixedText;
        var prefix = overrides.TryGetValue(OverrideField.Prefix, out var p) && !string.IsNullOrEmpty(p)
            ? p
            : Prefix;
        var suffix = overrides.TryGetValue(OverrideField.Suffix, out var s)
            ? (s ?? "")
            : Suffix;
        // MinWidth must be >= 1 (a value of 0 would produce an empty width
        // spec, "D0"). Invalid overrides are dropped — the persisted
        // MinWidth stays in effect. UpperLimit is purely informational; the
        // generator path applies it via the segment's MaxNumericValue.

        var minWidth = overrides.TryGetValue(OverrideField.MinWidth, out var mw) && int.TryParse(mw, out var parsed) && parsed >= 1
            ? parsed
            : MinWidth;

        return Type switch
        {
            SegmentType.Fixed => fixedText + suffix,
            SegmentType.NumericSequence => prefix + LastSequence.ToString($"D{minWidth}") + suffix,
            SegmentType.AlphabeticSequence => prefix + (LastPrefix ?? "") + suffix,
            SegmentType.AlphanumericSequence => (LastPrefix ?? prefix ?? "A") + LastSequence.ToString($"D{minWidth}") + suffix,
            _ => string.Empty
        };
    }

    // ── Tenant-override mutators (spec §4.12) ──
    // Called only by EntityCodeRule.ApplyOverrides during generation; the
    // values are transient (the next render reflects the override, the
    // segment is NOT persisted with the override applied). External code
    // should not call these directly — the rule aggregates the call.

    internal void SetFixedText(string value) => FixedText = value ?? "";
    internal void SetPrefix(string value) => Prefix = value ?? "";
    internal void SetSuffix(string value) => Suffix = value ?? "";
    internal void SetResetPeriod(ResetPeriod value) => ResetPeriod = value;
    internal void SetMinWidth(int value)
    {
        // Allow 0 (Fixed segments don't use MinWidth but the default is 0;
        // restoring the format snapshot to a Fixed segment must be a no-op
        // for MinWidth). The >= 1 guard still applies at ApplyOverrides time
        // (where a real override would be misconfigured at 0).
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "MinWidth must be >= 0.");
        MinWidth = value;
    }
    internal void SetUpperLimit(string? value) => UpperLimit = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The maximum numeric portion value. If <see cref="UpperLimit"/> is set it is
    /// parsed as an integer; otherwise the width implies the max (<c>10^MinWidth - 1</c>).
    /// </summary>
    private int MaxNumericValue =>
        UpperLimit is { } ul && int.TryParse(ul, out var parsed) ? parsed : (int)Math.Pow(10, MinWidth) - 1;

    /// <summary>The maximum alphabetic prefix. Defaults to <c>"Z"</c>.</summary>
    private string MaxAlphaValue => string.IsNullOrWhiteSpace(UpperLimit) ? "Z" : UpperLimit!;

    private string Describe() =>
        $"EntityCodeSegment {Id} (index {Index}, type {Type}) hit its upper limit '{UpperLimit}'.";

    /// <summary>
    /// Increments an Excel-style alphabetic label: A→B→…→Z→AA→AB→…→AZ→BA→…→ZZ→AAA.
    /// </summary>
    private static string NextAlpha(string current)
    {
        if (string.IsNullOrEmpty(current))
            return "A";

        var chars = current.ToCharArray();
        for (var i = chars.Length - 1; i >= 0; i--)
        {
            if (chars[i] < 'Z')
            {
                chars[i]++;
                return new string(chars);
            }
            chars[i] = 'A';
        }
        // Carried past the leftmost digit (e.g. Z → AA, ZZ → AAA).
        return "A" + new string(chars);
    }

    /// <summary>
    /// Compares two Excel-style alphabetic labels: longer is greater; same length
    /// compares lexicographically. Returns &gt;0 if <paramref name="a"/> is greater
    /// than <paramref name="b"/>.
    /// </summary>
    private static int CompareAlpha(string a, string b)
    {
        if (a.Length != b.Length) return a.Length - b.Length;
        return string.CompareOrdinal(a, b);
    }
}