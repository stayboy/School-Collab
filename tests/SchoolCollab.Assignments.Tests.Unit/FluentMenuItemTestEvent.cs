using AngleSharp.Dom;
using Bunit;
using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// bUnit seam for activating a v5 <c>FluentMenuItem</c>.
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
    /// event whose <c>MenuItemEventArgs.Id</c> is not the item's own.
    /// </summary>
    internal static void MenuItemClick(this IElement item) =>
        item.TriggerEvent("onmenuitemchange", new MenuItemEventArgs { Id = item.GetAttribute("id") });
}
