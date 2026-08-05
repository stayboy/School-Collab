using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Application.Components.Pages.EntityCodeRules;

/// <summary>
/// View-model for a single segment row in the
/// <see cref="SegmentEditor"/>. Mirrors the server-side
/// <c>EntityCodeSegmentInput</c> command record (via int enum values) so the
/// form can hand a list of these straight to the API client after a trivial
/// projection. Lives next to the editor component as a plain C# class so it
/// is reachable from sibling razor files in this assembly.
/// </summary>
public sealed class SegmentFormModel
{
    public int Index { get; set; }
    public string? Role { get; set; }

    /// <summary>Wire-format integer for <c>EntityCodeSegmentInput.Type</c>.</summary>
    public SegmentTypeDto Type { get; set; } = SegmentTypeDto.Fixed;

    public string? FixedText { get; set; }
    public string? Prefix { get; set; }
    public string? Suffix { get; set; }

    /// <summary>Wire-format integer for <c>EntityCodeSegmentInput.ResetPeriod</c>.</summary>
    public ResetPeriodDto ResetPeriod { get; set; } = ResetPeriodDto.None;

    public int MinWidth { get; set; } = 2;
    public string? UpperLimit { get; set; }
}

// ── Local copies of the server enums so the form doesn't pull in
//    SchoolCollab.Settings.Core directly. The integer values must match
//    SegmentType.cs / ResetPeriod.cs in Settings.Core (spec §3.2). ────────

public enum SegmentTypeDto
{
    Fixed = 0,
    NumericSequence = 1,
    AlphabeticSequence = 2,
    AlphanumericSequence = 3
}

public enum ResetPeriodDto
{
    None = 0,
    Yearly = 1,
    Monthly = 2,
    Quarterly = 3
}

/// <summary>
/// View-model for one per-tenant override row in the
/// <see cref="Edit"/> page's overrides editor (spec §4.12).
/// <para>
/// Used by both the inline summary card (just the data fields) and the
/// <c>OverrideDialog</c> modal (which adds <see cref="IsEdit"/> and
/// <see cref="SelectedSegment"/> for the form-binding).
/// </para>
/// </summary>
public sealed class OverrideFormModel
{
    public Guid Id { get; set; }

    /// <summary>
    /// True when the dialog was opened for an existing row (Edit); false
    /// for a fresh row (Add). Controls the Segment dropdown's read-only
    /// state and the footer button label.
    /// </summary>
    public bool IsEdit { get; set; }

    public Guid EntityCodeSegmentId { get; set; }
    public int SegmentIndex { get; set; }

    /// <summary>
    /// Dialog-only: the selected <see cref="SegmentOption"/> in the
    /// dropdown (two-way bound). The summary card does not use this
    /// (it renders <see cref="SegmentIndex"/> directly).
    /// </summary>
    public SegmentOption? SelectedSegment { get; set; }

    public OverrideFieldDto Field { get; set; } = OverrideFieldDto.FixedText;
    public string Value { get; set; } = "";
}

/// <summary>
/// Dropdown option for the segment select in the override dialog.
/// </summary>
public sealed record SegmentOption(Guid SegmentId, int Index, string Label);

/// <summary>
/// Result returned by the <c>OverrideDialog</c> on successful submit.
/// Wraps the modified <see cref="OverrideFormModel"/> so the parent
/// page can apply the change to its override list and call the API.
/// </summary>
public sealed record OverrideResult(OverrideFormModel FormModel);

/// <summary>
/// Renders representative codes from a rule's segments so admins can see
/// the rendered format before saving. Two entry points:
/// <list type="bullet">
///   <item><see cref="RenderFirst"/> — a one-shot preview for the Index page's
///     "Preview" column (uses the wire DTOs; one code per rule).</item>
///   <item><see cref="RenderNext5"/> — five consecutive codes for the Edit
///     page's "Preview next 5 codes" button (uses the form model; demonstrates
///     rollover, suffix application, and upper-limit collision).</item>
/// </list>
/// <para>
/// <b>Why this class calls the production <see cref="EntityCodeSegment.Advance"/>
/// directly</b> — an earlier version carried a private <c>SimSegment</c>
/// class that mirrored the server's increment + render logic. It drifted:
/// AlphanumericSequence previews showed <c>ASG00</c> instead of <c>ASGA01</c>
/// because the simulator rendered before incrementing and started the alpha
/// prefix at <c>""</c> instead of the server's <c>"A"</c>. Building real
/// <see cref="EntityCodeSegment"/> instances and calling <see cref="EntityCodeSegment.Advance"/>
/// guarantees parity by construction — the preview IS the production code.
/// </para>
/// </summary>
public static class EntityCodePreview
{
    /// <summary>
    /// A single representative first code from a rule's segments. Drives the
    /// Index page's "Preview" column. Builds a transient
    /// <see cref="EntityCodeRule"/> from the DTO segments, calls
    /// <see cref="EntityCodeRule.GenerateNext"/> once, returns the result.
    /// Returns <c>"—"</c> when the list is null or empty. Returns the empty
    /// string when segment construction fails (the factory throws on bad
    /// MinWidth, missing FixedText on Fixed, etc.) — the Index page surfaces
    /// the empty cell rather than the error boundary.
    /// </summary>
    public static string RenderFirst(IReadOnlyList<EntityCodeSegmentDto>? segments)
    {
        if (segments is null || segments.Count == 0) return "—";

        EntityCodeRule rule;
        try
        {
            rule = BuildRuleFromDtos(segments);
        }
        catch
        {
            return string.Empty;
        }

        try
        {
            return rule.GenerateNext(DateTimeOffset.UtcNow);
        }
        catch (EntityCodeGenerationCollisionException)
        {
            // Upper limit already hit (no segments could be advanced). The
            // Index page renders this as the literal code; admins see the
            // collision and edit the template.
            return string.Empty;
        }
    }

    /// <summary>
    /// Renders the next 5 representative codes a template would produce from
    /// a fresh state. Drives the Edit page's "Preview next 5 codes" button
    /// (and the Create page via <c>SegmentsList.razor</c>). Returns an empty
    /// list when <paramref name="segments"/> is null or empty, or when
    /// segment construction fails.
    /// <para>
    /// The preview does not know the server's persisted
    /// <c>LastSequence</c> / <c>LastPrefix</c> — it validates the FORMAT, not
    /// the live continuation (spec §1.2 non-goal). Existing students/staff/
    /// assignments continue from the server's stored state; the preview only
    /// demonstrates what the next 5 generations WOULD look like if the
    /// template were applied to a fresh state.
    /// </para>
    /// <para>
    /// Stops early on <see cref="EntityCodeGenerationCollisionException"/>
    /// (upper limit reached) so the returned list reflects the realistic
    /// count of codes the template can still produce — e.g. a template with
    /// UpperLimit <c>"03"</c> returns at most 3 codes before the
    /// alphanumeric rolls over.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> RenderNext5(IReadOnlyList<SegmentFormModel>? segments)
    {
        if (segments is null || segments.Count == 0) return Array.Empty<string>();

        EntityCodeRule rule;
        try
        {
            rule = BuildRuleFromFormModels(segments);
        }
        catch
        {
            // Factory threw (e.g. MinWidth < 1 on a numeric segment). The
            // Edit/Create page surfaces this as an empty preview rather than
            // crashing the page mid-edit.
            return Array.Empty<string>();
        }

        var result = new List<string>(5);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            try
            {
                result.Add(rule.GenerateNext(now));
            }
            catch (EntityCodeGenerationCollisionException)
            {
                // Upper limit hit mid-sequence (e.g. rollover exhausted the
                // Z letter on a tight UpperLimit). Stop adding codes; the
                // list reflects how many codes the template can still
                // produce from a fresh state.
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// Builds an <see cref="EntityCodeRule"/> from the wire DTOs returned by
    /// the admin API. Used by <see cref="RenderFirst"/>. The rule itself is
    /// throwaway — only the segments' <see cref="EntityCodeSegment.Advance"/>
    /// logic matters for the preview.
    /// </summary>
    private static EntityCodeRule BuildRuleFromDtos(IReadOnlyList<EntityCodeSegmentDto> dtos)
    {
        var rule = EntityCodeRule.Create("PREVIEW", "Preview", null, isActive: true);
        foreach (var s in dtos.OrderBy(d => d.Index))
        {
            var seg = (SegmentType)s.Type switch
            {
                SegmentType.Fixed => EntityCodeSegment.Fixed(
                    s.Index, s.Role, s.FixedText ?? "", s.Suffix ?? ""),
                SegmentType.NumericSequence => EntityCodeSegment.Sequence(
                    s.Index, s.Role, SegmentType.NumericSequence,
                    prefix: s.Prefix ?? "",
                    suffix: s.Suffix ?? "",
                    resetPeriod: (ResetPeriod)s.ResetPeriod,
                    minWidth: Math.Max(1, s.MinWidth),
                    upperLimit: s.UpperLimit),
                SegmentType.AlphabeticSequence => EntityCodeSegment.Sequence(
                    s.Index, s.Role, SegmentType.AlphabeticSequence,
                    prefix: s.Prefix ?? "",
                    suffix: s.Suffix ?? "",
                    resetPeriod: (ResetPeriod)s.ResetPeriod,
                    minWidth: Math.Max(1, s.MinWidth),
                    upperLimit: s.UpperLimit),
                SegmentType.AlphanumericSequence => EntityCodeSegment.Sequence(
                    s.Index, s.Role, SegmentType.AlphanumericSequence,
                    prefix: s.Prefix ?? "",
                    suffix: s.Suffix ?? "",
                    resetPeriod: (ResetPeriod)s.ResetPeriod,
                    minWidth: Math.Max(1, s.MinWidth),
                    upperLimit: s.UpperLimit),
                _ => throw new InvalidOperationException($"Unknown SegmentType value {(int)s.Type}."),
            };
            rule.AddSegment(seg);
        }
        return rule;
    }

    /// <summary>
    /// Builds an <see cref="EntityCodeRule"/> from the Edit/Create page's
    /// <see cref="SegmentFormModel"/> view-models. Used by
    /// <see cref="RenderNext5"/>. The form models carry the wire-shape
    /// enums (<see cref="SegmentTypeDto"/> / <see cref="ResetPeriodDto"/>)
    /// so we cast them back to the production enum values; both have
    /// matching integer values (spec §3.2).
    /// </summary>
    private static EntityCodeRule BuildRuleFromFormModels(IReadOnlyList<SegmentFormModel> formModels)
    {
        var rule = EntityCodeRule.Create("PREVIEW", "Preview", null, isActive: true);
        foreach (var s in formModels.OrderBy(m => m.Index))
        {
            var seg = s.Type switch
            {
                SegmentTypeDto.Fixed => EntityCodeSegment.Fixed(
                    s.Index, s.Role, s.FixedText ?? "", s.Suffix ?? ""),
                SegmentTypeDto.NumericSequence => EntityCodeSegment.Sequence(
                    s.Index, s.Role, SegmentType.NumericSequence,
                    prefix: s.Prefix ?? "",
                    suffix: s.Suffix ?? "",
                    resetPeriod: (ResetPeriod)s.ResetPeriod,
                    minWidth: s.MinWidth,
                    upperLimit: s.UpperLimit),
                SegmentTypeDto.AlphabeticSequence => EntityCodeSegment.Sequence(
                    s.Index, s.Role, SegmentType.AlphabeticSequence,
                    prefix: s.Prefix ?? "",
                    suffix: s.Suffix ?? "",
                    resetPeriod: (ResetPeriod)s.ResetPeriod,
                    minWidth: s.MinWidth,
                    upperLimit: s.UpperLimit),
                SegmentTypeDto.AlphanumericSequence => EntityCodeSegment.Sequence(
                    s.Index, s.Role, SegmentType.AlphanumericSequence,
                    prefix: s.Prefix ?? "",
                    suffix: s.Suffix ?? "",
                    resetPeriod: (ResetPeriod)s.ResetPeriod,
                    minWidth: s.MinWidth,
                    upperLimit: s.UpperLimit),
                _ => throw new InvalidOperationException($"Unknown SegmentTypeDto value {(int)s.Type}."),
            };
            rule.AddSegment(seg);
        }
        return rule;
    }
}