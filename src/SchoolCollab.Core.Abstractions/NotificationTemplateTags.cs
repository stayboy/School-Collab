namespace SchoolCollab.Core.Notifications;

/// <summary>
/// Template tag vocabulary — spec §3.2 read literally: dot-free PascalCase, <b>ten</b> tags,
/// case-insensitive matching, unknown tags left literal. Tags that expand to markup
/// (<c>{{Instructions}}</c>, <c>{{Description}}</c>) are email-only-authored rich HTML;
/// <c>{{Link}}</c> is channel-appropriate (email long URL vs SMS/WhatsApp short URL).
/// <para>The Round-A working spellings (<c>AssignmentTitle</c>, <c>DescriptionExcerpt</c>,
/// <c>ContactName</c>) survive as render aliases — see <see cref="Aliases"/> — so a template
/// authored against the earlier vocabulary still renders; <see cref="All"/> carries the spec
/// set only, because the editor's placeholder-tag menu reads it.</para>
/// <para>Round <c>notification-templates-b3b</c> (Q90a) moved this set here from
/// <c>Assignments.Core/Domain</c>: it is this feature's cross-context vocabulary beside
/// <see cref="NotificationChannel"/>, so the Settings editor can offer the tags without a
/// reference to Assignments and the renderer keeps its canonical spelling. The class is
/// zero-dependency and static — the move changes no wire and no behaviour.</para>
/// </summary>
public static class NotificationTemplateTags
{
    /// <summary>Assignment title.</summary>
    public const string Title = "Title";

    /// <summary>Assignment description (full value; the built-in preview body caps its own excerpt).</summary>
    public const string Description = "Description";

    /// <summary>Assignment due date — empty when the assignment has none.</summary>
    public const string DueDate = "DueDate";

    /// <summary>Student-facing instructions (HTML, email only — stripped from SMS/WhatsApp).</summary>
    public const string Instructions = "Instructions";

    /// <summary>The recipient's ward (student display name).</summary>
    public const string StudentName = "StudentName";

    /// <summary>The guardian contact's display name — empty on student-owned recipients.</summary>
    public const string GuardianName = "GuardianName";

    /// <summary>The guardian's role text (Primary / CC) — empty on student-owned recipients.</summary>
    public const string GuardianRole = "GuardianRole";

    /// <summary>The tenant display name (from the tenant context's <c>tenant_name</c>).</summary>
    public const string SchoolName = "SchoolName";

    /// <summary>Channel-appropriate deep link: email = long absolute/relative URL,
    /// SMS/WhatsApp = short <c>/l/{code}</c> URL.</summary>
    public const string Link = "Link";

    /// <summary>The link's expiry date — empty when there is none.</summary>
    public const string LinkExpiry = "LinkExpiry";

    /// <summary>The spec §3.2 vocabulary, in spec order — the source for the editor's placeholder-tag menu.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Title, Description, DueDate, Instructions, StudentName, GuardianName, GuardianRole, SchoolName, Link, LinkExpiry,
    ];

    /// <summary>
    /// The retired Round-A spellings mapped onto their §3.2 tag. Kept here — one map, not
    /// literals scattered through the renderer — so a template authored before the rename
    /// still resolves instead of leaking the literal tag into a parent's message.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AssignmentTitle"] = Title,
            ["DescriptionExcerpt"] = Description,
            ["ContactName"] = GuardianName,
        };

    /// <summary>
    /// Every known spelling (the §3.2 set plus the <see cref="Aliases"/>), case-insensitively
    /// mapped onto the canonical constant — so the renderer matches exact names and a tag written
    /// as <c>{{title}}</c> resolves to <see cref="Title"/>.
    /// </summary>
    private static readonly Dictionary<string, string> KnownTags = BuildKnownTags();

    /// <summary>
    /// Maps a tag name as written in a template onto its §3.2 spelling: a known tag (canonical or
    /// alias, any casing) resolves to the constant, an unknown tag is returned unchanged and stays
    /// literal at render time.
    /// </summary>
    public static string Canonicalize(string tagName) =>
        KnownTags.TryGetValue(tagName, out var canonical) ? canonical : tagName;

    private static Dictionary<string, string> BuildKnownTags()
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in All)
        {
            known[tag] = tag;
        }

        foreach (var alias in Aliases)
        {
            known[alias.Key] = alias.Value;
        }

        return known;
    }
}
