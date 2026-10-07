namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// The CLOSED set of categories a "Targets &amp; audience" entry may carry — exactly the five
/// <c>TargetKindDto</c> kinds an assignment can persist (grill decision Q2: the free-text Subjects
/// category is dropped because <c>TargetKindDto</c> has no Subject; Q7 = new categories require a
/// code change). The dialog shows only the field that applies to the selected category — never all
/// fields at once.
/// </summary>
public enum TargetsAndAudienceCategory
{
    GradeLevels,
    Streams,
    Students,
    ActivityGroups,
    Everyone
}

/// <summary>
/// One selectable value for the dialog's typed picker: the option's key as a string (a
/// <see cref="Guid"/> for every category that references an entity) plus the label the author reads.
/// The dialog cannot share the page's option record — <c>PickerOption</c> is nested in
/// <c>AssignmentAuthoring</c> — so the page maps its options onto this shape when it opens the dialog.
/// </summary>
public sealed record TargetsAndAudienceOption(string Value, string Label);

/// <summary>
/// One authored "Targets &amp; audience" entry: a category plus the TYPED value it carries
/// (grill decision Q1: a real picker per category, never free text).
/// <para><see cref="RefId"/> is the referenced entity's id and is null exactly for
/// <see cref="TargetsAndAudienceCategory.Everyone"/> — the "applies to all" checkpoint references no
/// entity (it is persisted as the single <c>AllStudents</c> target row, whose RefId is null too).</para>
/// <para><see cref="Label"/> is the author-facing name captured when the value was picked, so the
/// builder renders an entry without re-reading the option sources.</para>
/// </summary>
public sealed record TargetsAndAudienceEntry(TargetsAndAudienceCategory Category, Guid? RefId, string Label)
{
    /// <summary>
    /// The label of the single Everyone entry — that category's value IS its label. Declared once so
    /// the dialog that authors the entry and the page that renders it cannot spell it two ways.
    /// </summary>
    public const string EveryoneLabel = "Everyone";
}

/// <summary>
/// The Add dialog's success payload: EVERY chip one submission authored, in pick order. The entity
/// pickers are single-value and append each pick to the chip row, so one submission yields one entry per
/// chip. A submission may therefore be a <b>mixed-kind</b> batch — the model and the page accept
/// entries of several categories at once, while the dialog CLEARS its picked-targets zone on every
/// category switch, so one dialog submission carries a single kind (round <c>drop-primary-grade</c>
/// replaced the multi-select listboxes with a single dropdown + dismissible chips). It never mixes with
/// <see cref="TargetsAndAudienceCategory.Everyone"/>: that value-less checkpoint clears every entity chip
/// when picked, and any entity pick clears it (TGT-2), so the batch carries either the one Everyone entry
/// or one or more entity entries.
/// </summary>
public sealed record TargetsAndAudienceDialogResult(IReadOnlyList<TargetsAndAudienceEntry> Entries);
