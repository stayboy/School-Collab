namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// R3 (D8(a)) — the one extraction knob that is safe to vary per deployment: the per-file wall-clock
/// budget. Bound in <c>AddAssignmentsCore</c> from the <see cref="SectionName"/> section.
/// <para>The size and character <b>caps</b> deliberately stay compile-time constants on
/// <see cref="AttachmentExtractionLimits"/>: the <c>ExtractedText</c>/<c>ExtractionError</c> column
/// lengths are taken from them by the EF configuration, so a configurable cap could be raised past the
/// column it is persisted into. The timeout has no such coupling.</para>
/// </summary>
public sealed class AttachmentExtractionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Assignments:AttachmentExtraction";

    /// <summary>How long one extraction attempt may run before it is abandoned and reported as
    /// <see cref="AttachmentExtractionStatus.Failed"/> (seconds). Defaults to
    /// <see cref="AttachmentExtractionLimits.TimeoutSeconds"/>.</summary>
    public int TimeoutSeconds { get; set; } = AttachmentExtractionLimits.TimeoutSeconds;
}
