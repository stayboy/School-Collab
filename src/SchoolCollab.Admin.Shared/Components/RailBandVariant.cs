namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// The tint of a <see cref="RailBand"/>. A variant changes the band's colour ONLY — every band keeps its
/// text label, so colour never carries the meaning on its own (it must still read in greyscale and to a
/// screen reader). Values map to the <c>rail-band--{variant}</c> classes in <c>wwwroot/app.css</c>.
/// </summary>
/// <remarks>
/// <c>Required</c> and <c>Assumed</c> are the round-B enrolment bands
/// (<c>documents/specs/subject-enrollment-requirement-flag.md</c>). They are declared here so the
/// round-B change adds a call site instead of changing this contract — the whole argument for extracting
/// the component when only <c>Neutral</c> and <c>Groups</c> were in use.
/// </remarks>
public enum RailBandVariant
{
    /// <summary>The default: a plain band of facts (e.g. <em>Placement</em>).</summary>
    Neutral,

    /// <summary>Activity-group membership (violet) — the round-A band on the student edit rail.</summary>
    Groups,

    /// <summary>Subjects that need an explicit enrolment action (amber). Round B.</summary>
    Required,

    /// <summary>Subjects enrolled by implication, not by a record (teal). Round B.</summary>
    Assumed,
}
