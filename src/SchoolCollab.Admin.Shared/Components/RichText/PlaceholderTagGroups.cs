using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Admin.Shared.Components.RichText;

/// <summary>
/// One collapsible group of the placeholder-tag menu: the sub-menu's label and the tags it holds.
/// </summary>
/// <param name="Name">The group heading, e.g. <c>Recipients</c>.</param>
/// <param name="Tags">The bare tag names in this group, in menu order (no <c>{{ }}</c> braces).</param>
public sealed record PlaceholderTagGroup(string Name, IReadOnlyList<string> Tags);

/// <summary>
/// The placeholder-tag menu's ONE group map, plus the projection from a flat tag list onto the groups
/// the menu renders.
/// <para>The vocabulary stays <see cref="NotificationTemplateTags"/> — this type only says how the
/// editor groups it, so a tag added to the vocabulary but not to a group here still reaches the menu
/// (under <see cref="OtherGroupName"/>) instead of becoming unreachable.</para>
/// </summary>
public static class PlaceholderTagGroups
{
    /// <summary>The trailing group that catches every tag no declared group names.</summary>
    public const string OtherGroupName = "Other";

    /// <summary>The declared groups, in menu order. This is the one place the grouping is written.</summary>
    public static readonly IReadOnlyList<PlaceholderTagGroup> All =
    [
        new("Assignment",
        [
            NotificationTemplateTags.Title,
            NotificationTemplateTags.Description,
            NotificationTemplateTags.DueDate,
        ]),
        new("Content",
        [
            NotificationTemplateTags.Instructions,
        ]),
        new("Recipients",
        [
            NotificationTemplateTags.StudentName,
            NotificationTemplateTags.GuardianName,
            NotificationTemplateTags.GuardianRole,
        ]),
        new("School & links",
        [
            NotificationTemplateTags.SchoolName,
            NotificationTemplateTags.Link,
            NotificationTemplateTags.LinkExpiry,
        ]),
    ];

    /// <summary>
    /// The groups to render for <paramref name="tags"/>: the declared groups in declaration order, each
    /// narrowed to the tags the caller offers, then one <see cref="OtherGroupName"/> group holding every
    /// offered tag the map does not name. Empty groups are dropped, and a repeated tag appears once.
    /// </summary>
    /// <param name="tags">The tags the menu may offer — normally <see cref="NotificationTemplateTags.All"/>.</param>
    public static IReadOnlyList<PlaceholderTagGroup> For(IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var remaining = tags.Distinct(StringComparer.Ordinal).ToList();
        var groups = new List<PlaceholderTagGroup>(All.Count + 1);

        foreach (var group in All)
        {
            var present = new List<string>();
            foreach (var tag in group.Tags)
            {
                if (remaining.Remove(tag))
                {
                    present.Add(tag);
                }
            }

            if (present.Count > 0)
            {
                groups.Add(new PlaceholderTagGroup(group.Name, present));
            }
        }

        if (remaining.Count > 0)
        {
            groups.Add(new PlaceholderTagGroup(OtherGroupName, remaining));
        }

        return groups;
    }
}
