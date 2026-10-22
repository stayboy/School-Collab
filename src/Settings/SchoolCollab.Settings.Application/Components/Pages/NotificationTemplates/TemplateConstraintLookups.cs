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

    /// <summary>True once every option set loaded; false means the pickers must stay disabled.</summary>
    public bool Loaded { get; private set; }

    /// <summary>The failure that left <see cref="Loaded"/> false, for the inline warning.</summary>
    public string? Error { get; private set; }

    /// <summary>Loads every option set in parallel. A failure degrades to <see cref="Error"/>.</summary>
    public async Task LoadAsync()
    {
        if (Loaded)
            return;

        try
        {
            var grades = students.ListGradeLevelsAsync();
            var subjects = students.ListTopicsAsync();
            var groups = students.ListActivityGroupsAsync();
            await Task.WhenAll(grades, subjects, groups);

            var streams = await codedValues.GetChildrenByParentCodeAsync(CodedValueParent.Streams.ToCode());

            Grades = ToOptions(grades.Result);
            Subjects = ToOptions(subjects.Result);
            Groups = ToOptions(groups.Result.Where(g => g.IsActive is not false));
            Streams = (streams ?? [])
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new OptionItem(c.Id.ToString("D"), c.Name))
                .ToArray();

            Error = null;
            Loaded = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
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