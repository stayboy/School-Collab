namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Audience/content dimensions a notification template can be constrained by (spec §3.2 / Q15).
/// Grade levels, streams and activity groups are audience dimensions; subject/topic and
/// assignment type are content dimensions. Individual students are never a constraint dimension.
/// </summary>
public enum TemplateConstraintDimension
{
    /// <summary>Grade level (e.g. "Grade 3").</summary>
    GradeLevel = 0,

    /// <summary>Stream (e.g. "Science").</summary>
    Stream = 1,

    /// <summary>Activity group (e.g. "After-school club").</summary>
    ActivityGroup = 2,

    /// <summary>Subject / topic (e.g. "Mathematics").</summary>
    Subject = 3,

    /// <summary>Assignment type (Digital / SemiManual / Manual).</summary>
    AssignmentType = 4
}