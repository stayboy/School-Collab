namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// Template tag vocabulary (spec §3.2 / Q4/Q7): dot-free PascalCase, case-insensitive matching,
/// unknown tags left literal. One tag per sendout — resolved at render time against the assignment
/// and contact. Tags that expand to HTML ({{Instructions}}) are email-only; {{Link}} is
/// channel-appropriate (email long URL vs SMS/WhatsApp short URL).
/// </summary>
public static class NotificationTemplateTags
{
    /// <summary>Assignment title.</summary>
    public const string AssignmentTitle = "AssignmentTitle";

    /// <summary>Assignment due date (short date).</summary>
    public const string DueDate = "DueDate";

    /// <summary>Capped description excerpt (email preview body).</summary>
    public const string DescriptionExcerpt = "DescriptionExcerpt";

    /// <summary>Full assignment instructions (HTML, email only).</summary>
    public const string Instructions = "Instructions";

    /// <summary>Channel-appropriate deep link: email = long absolute/relative URL,
    /// SMS/WhatsApp = short /l/{code} URL.</summary>
    public const string Link = "Link";

    /// <summary>All tags in the vocabulary, for the tag-reference chips in the editor.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { AssignmentTitle, DueDate, DescriptionExcerpt, Instructions, Link };
}