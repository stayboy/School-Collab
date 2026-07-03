using Microsoft.AspNetCore.Components.Routing;
using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Components.Dashboard;

/// <summary>
/// Describes a single dashboard navigation card rendered by
/// <see cref="DashboardSection"/> when its <see cref="DashboardSection.Items"/>
/// parameter is supplied. Each item maps one-to-one to a <see cref="DashboardCard"/>.
/// </summary>
/// <remarks>
/// Holding the icon as an <see cref="Icon"/> <em>instance</em> (rather than the
/// generic <c>&lt;DashboardCard TIcon&gt;</c> type-parameter the component used
/// previously) lets a page build one heterogeneous list mixing icons of
/// different concrete types (e.g. <c>Icons.Regular.Size24.Tag</c> and
/// <c>Icons.Regular.Size24.People</c>), which a single
/// <c>List&lt;DashboardItem&lt;TIcon&gt;&gt;</c> cannot express. The
/// <see cref="DashboardCard"/> renders the instance via
/// <c>&lt;FluentIcon Value="..." /&gt;</c>.
/// </remarks>
/// <param name="Href">Relative URL the card links to.</param>
/// <param name="Title">Card heading.</param>
/// <param name="Description">
/// Card description. The visible text is line-clamped to keep cards in a row
/// equal height; the full text is also exposed via the card's <c>title</c>
/// tooltip attribute.
/// </param>
/// <param name="Icon">Fluent UI icon instance shown on the card.</param>
/// <param name="Match">NavLink match mode. Defaults to <see cref="NavLinkMatch.Prefix"/>.</param>
/// <param name="IconWidth">Rendered icon width (CSS length). Defaults to <c>48px</c>.</param>
/// <param name="MaxDescriptionLength">
/// Soft cap on the visible description length. See
/// <see cref="DashboardCard.MaxDescriptionLength"/>.
/// </param>
public sealed record DashboardItem(
    string Href,
    string Title,
    string Description,
    Icon Icon,
    NavLinkMatch Match = NavLinkMatch.Prefix,
    string IconWidth = "48px",
    int MaxDescriptionLength = 120);