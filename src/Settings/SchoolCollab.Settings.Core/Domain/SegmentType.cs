namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// The kind of value an <see cref="EntityCodeSegment"/> renders.
/// </summary>
public enum SegmentType
{
    /// <summary>Static text that never changes (e.g. the stamp "STU").</summary>
    Fixed = 0,

    /// <summary>Auto-incrementing zero-padded number (e.g. 01, 02, …, 99).</summary>
    NumericSequence = 1,

    /// <summary>Auto-incrementing alphabetic series (e.g. A, B, …, Z, AA, AB).</summary>
    AlphabeticSequence = 2,

    /// <summary>
    /// Auto-incrementing prefix+number with rollover
    /// (e.g. A01, A02, …, A09, B01, B02, …).
    /// </summary>
    AlphanumericSequence = 3,

    /// <summary>
    /// Initials derived from the entity's display name (e.g. "computer science"
    /// → <c>CS</c>, single-word "Mathematics" → <c>MA</c>). Rendered at generation
    /// time from the <c>nameHint</c> supplied to the generator; the segment carries
    /// no sequence state of its own — pair it with a <see cref="NumericSequence"/>
    /// to get e.g. <c>CS01</c>. Articles/prepositions (and, of, the) are skipped.
    /// </summary>
    WordInitials = 4
}