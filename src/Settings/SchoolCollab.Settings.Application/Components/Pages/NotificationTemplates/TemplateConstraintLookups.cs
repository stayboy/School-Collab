using SchoolCollab.Admin.Shared.Constants;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

/// <summary>
/// The constraint vocabulary for the authoring surface (redesign spec §3.6): one load of the four option
/// sets, shared by the editor''s pickers and the read-only constraint dialog so a stored value is rendered
/// with the same name in both places — the drift class the old page avoided by owning both halves.
/// <para>
/// Encodings are the matcher''s, unchanged: grade level / activity group / subject are entity ids,
/// stream is a coded-value id (the codebook is its owner), assignment type is the enum name. A value
/// with no matching option renders as its raw stored text, so a lookup outage degrades the LABEL and
/// never drops a constraint.
/// </para>
/// <para>
/// Each dimension loads on its OWN (owner-visible defect, 2026-10-23). They span two hosts and three
/// feature postures — <c>/activity-groups</c> is not even mapped unless
/// <c>FEATURE:EnableActivityGroups</c> is on — so one absent route must not blank the rest. The single
/// try/catch that used to wrap all four did exactly that: a 404 on activity groups left every picker
/// empty, disabled the whole add menu, and reported it as "constraint names are unavailable".
/// </para>
/// </summary>
public sealed class TemplateConstraintLookups(StudentsLookupClient students, CodedValuesApiClient codedValues)
{
    /// <summary>One selectable value: the stored encoding plus its author-facing name.</summary>
    public sealed record OptionItem(string Value, string Text);

    /// <summary>Assignment types are a closed set, so they need no lookup (spec §3.6).</summary>
    public static readonly OptionItem[] AssignmentTypes =
    [
        new("Digital", "Digital"),
        new("SemiManual", "Semi-manual"),
        new("Manual", "Manual"),
    ];

    public OptionItem[] Grades { get; private set; } = [];
    public OptionItem[] Streams { get; private set; } = [];
    public OptionItem[] Groups { get; private set; } = [];
    public OptionItem[] Subjects { get; private set; } = [];

    /// <summary>True once the load pass has run. Before that every dimension reads as unavailable,
    /// so nothing offers a picker it has not filled.</summary>
    public bool Loaded { get; private set; }

    /// <summary>
    /// The OUTAGE, for the inline warning: the dimensions whose lookup failed for a reason the school
    /// cannot be expected to have chosen. Null when nothing failed.
    /// <para>
    /// A dimension that is merely not mapped on this host is deliberately NOT reported here. It is a
    /// feature posture, not a fault — and reporting it was the defect: an optional feature being off
    /// produced a warning that read as though every constraint name was gone.
    /// </para>
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>Why a dimension has no options, keyed by dimension. Absent means the dimension is
    /// usable; present means it is not, and <see cref="Unavailable"/> says whether that is permanent.</summary>
    private readonly Dictionary<TemplateConstraintDimension, Unavailable> _unavailable = [];

    /// <summary>One dimension that could not be loaded: the reason, and whether it is a fault.</summary>
    private readonly record struct Unavailable(string Reason, bool IsOutage);

    /// <summary>Whether the dimension can be picked right now.</summary>
    public bool IsAvailable(TemplateConstraintDimension dimension) =>
        Loaded && !_unavailable.ContainsKey(dimension);

    /// <summary>
    /// Whether the add menu should OFFER the dimension at all. A dimension the host does not serve is
    /// hidden — offering it would advertise a capability the school does not have. One that failed
    /// (an outage) stays, disabled with its reason, so the capability is visibly there and the reason
    /// is readable rather than the feature silently disappearing.
    /// </summary>
    public bool IsOffered(TemplateConstraintDimension dimension) =>
        IsAvailable(dimension)
        || (_unavailable.TryGetValue(dimension, out var unavailable) && unavailable.IsOutage);

    /// <summary>The reason a dimension cannot be picked, for its disabled tooltip.</summary>
    public string? UnavailableReason(TemplateConstraintDimension dimension) =>
        _unavailable.TryGetValue(dimension, out var unavailable) ? unavailable.Reason : null;

    /// <summary>Loads every option set, each independently — one dimension's absence leaves the
    /// others intact. A failure degrades to <see cref="Error"/> and that dimension alone.</summary>
    public async Task LoadAsync()
    {
        if (Loaded)
            return;

        await Task.WhenAll(
            LoadDimensionAsync(
                TemplateConstraintDimension.GradeLevel,
                async () =>
                {
                    var grades = await students.ListGradeLevelsAsync();
                    return grades is null ? null : ToOptions(grades);
                },
                options => Grades = options),
            LoadDimensionAsync(
                TemplateConstraintDimension.Subject,
                async () =>
                {
                    var topics = await students.ListTopicsAsync();
                    return topics is null ? null : ToOptions(topics);
                },
                options => Subjects = options),
            LoadDimensionAsync(
                TemplateConstraintDimension.ActivityGroup,
                async () =>
                {
                    var groups = await students.ListActivityGroupsAsync();
                    return groups is null ? null : ToOptions(groups.Where(g => g.IsActive is not false));
                },
                options => Groups = options),
            LoadDimensionAsync(
                TemplateConstraintDimension.Stream,
                LoadStreamsAsync,
                options => Streams = options));

        var outages = _unavailable.Where(entry => entry.Value.IsOutage).ToArray();
        Error = outages.Length == 0
            ? null
            : string.Join("; ", outages
                .OrderBy(entry => entry.Key)
                .Select(entry => $"{DimensionLabel(entry.Key)}: {entry.Value.Reason}"));

        Loaded = true;
    }

    /// <summary>
    /// The stream catalogue, already projected. Null never means "route absent" here: the
    /// coded-values route always exists, and an empty codebook is an empty list, not an absence.
    /// </summary>
    private async Task<OptionItem[]?> LoadStreamsAsync()
    {
        var children = await codedValues.GetChildrenByParentCodeAsync(CodedValueParent.Streams.ToCode());
        return (children ?? [])
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new OptionItem(c.Id.ToString("D"), c.Name))
            .ToArray();
    }

    /// <summary>
    /// Loads ONE dimension's options. <paramref name="load"/> returns null for "this host does not
    /// serve that route" (the client's 404 convention) — recorded as a permanent absence and NOT an
    /// error. Anything else is an outage.
    /// </summary>
    private async Task LoadDimensionAsync(
        TemplateConstraintDimension dimension,
        Func<Task<OptionItem[]?>> load,
        Action<OptionItem[]> assign)
    {
        try
        {
            var options = await load();
            if (options is null)
            {
                _unavailable[dimension] = new Unavailable(
                    $"{DimensionLabel(dimension)} is not available for this school.", IsOutage: false);
                return;
            }

            assign(options);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _unavailable[dimension] = new Unavailable(ex.Message, IsOutage: true);
        }
    }

    /// <summary>The options behind one dimension.</summary>
    public OptionItem[] OptionsFor(SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension dimension) => dimension switch
    {
        SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension.GradeLevel => Grades,
        SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension.Stream => Streams,
        SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension.ActivityGroup => Groups,
        SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension.Subject => Subjects,
        SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension.AssignmentType => AssignmentTypes,
        _ => [],
    };

    /// <summary>The stored value''s display name, or the raw value when nothing matches.</summary>
    public string DisplayFor(SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension dimension, string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return "(not set)";

        var option = OptionsFor(dimension)
            .FirstOrDefault(o => string.Equals(o.Value, trimmed, StringComparison.OrdinalIgnoreCase));
        return option?.Text ?? trimmed;
    }

    /// <summary>The dimension''s author-facing name (the one map used by the editor and the dialog).</summary>
    public static string DimensionLabel(SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension dimension) => dimension switch
    {
        SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension.GradeLevel => "Grade level",
        SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension.Stream => "Stream",
        SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension.ActivityGroup => "Activity group",
        SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension.Subject => "Subject",
        SchoolCollab.Admin.Shared.Services.TemplateConstraintDimension.AssignmentType => "Assignment type",
        _ => dimension.ToString(),
    };

    private static OptionItem[] ToOptions(IEnumerable<LookupOptionDto> options) =>
        options
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o => new OptionItem(o.Id.ToString("D"), o.Name))
            .ToArray();
}
