using AngleSharp.Dom;
using Bunit;
using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit seams for a v5 <c>FluentMenuItem</c>: activating it, and reading the label it actually shows.
/// </summary>
/// <remarks>
/// FluentUI v4 attached the item's <c>OnClick</c> to the DOM <c>click</c> event, so a test drove an
/// item with bUnit's <c>Click()</c>. v5 dropped that handler: <c>FluentMenuItem.razor</c> renders
/// <c>@onmenuitemchange</c> only, and the library raises it from the DOM <c>change</c> event
/// (<c>FluentUICustomEvents.MenuItem</c> in Core.Scripts) — bUnit's JS-less DOM never fires it.
/// <c>Click()</c> therefore raises nothing, so a menu-item test silently does nothing: it passes
/// vacuously when it asserts the absence of an effect, and fails on an action that never ran.
/// </remarks>
internal static class FluentMenuItemTestEvent
{
    /// <summary>
    /// Raises <c>onmenuitemchange</c> on <paramref name="item"/> — the v5 equivalent of clicking it.
    /// The id is read from the element because <c>FluentMenuItem.OnChangeHandlerAsync</c> drops any
    /// event whose <c>MenuItemEventArgs.Id</c> is not the item's own. <paramref name="text"/> stands in
    /// for the item's <c>innerText</c>, which the library's JS reads from the DOM (<c>text: target.innerText</c>)
    /// and which a JS-less bUnit DOM therefore cannot supply — pass it when the code under test reads
    /// <c>MenuItemEventArgs.Text</c>.
    /// </summary>
    internal static void MenuItemClick(this IElement item, string? text = null) =>
        item.TriggerEvent("onmenuitemchange", new MenuItemEventArgs { Id = item.GetAttribute("id"), Text = text });

    /// <summary>
    /// The label an item itself shows — its DIRECT text, not <c>TextContent</c>.
    /// <para>The distinction is the whole reason this seam exists: a menu item that carries a nested
    /// sub-menu (<c>FluentMenuItem.MenuItems</c>) reports the whole sub-tree as its text, so a
    /// <c>TextContent</c> comparison matches a group header and its children as one string.</para>
    /// </summary>
    internal static string OwnLabel(this IElement item) =>
        string.Concat(item.ChildNodes.OfType<IText>().Select(t => t.TextContent)).Trim();
}
