namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// The kind of mutation recorded in a <see cref="TemplateAuditEntry"/>. Append-only;
/// values are never removed (mirrors <see cref="FlagChangeKind"/>).
/// </summary>
public enum TemplateChangeKind
{
    /// <summary>Template created.</summary>
    Created = 0,

    /// <summary>Template updated (subject, body, or constraints changed).</summary>
    Updated = 1,

    /// <summary>Template deleted (reset to built-in default).</summary>
    Deleted = 2
}