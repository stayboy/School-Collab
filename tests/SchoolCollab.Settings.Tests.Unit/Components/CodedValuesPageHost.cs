using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using IndexPage = SchoolCollab.Settings.Admin.Components.Pages.CodedValues.Index;

namespace SchoolCollab.Settings.Tests.Unit.Components;

/// <summary>
/// Test host that renders the Coded Values <see cref="IndexPage"/> together
/// with the section outlets the production <c>SchoolCollabLayout</c> provides.
/// The real layout publishes three named slots (<c>page-toolbar</c>,
/// <c>page-footer</c>, plus the routed body), and the page publishes its
/// toolbar and inline chat via <c>SectionContent</c> targeted at those slots.
/// bUnit renders the page standalone (no layout / no router), so without this
/// host the page's <c>SectionContent</c> has no matching outlet and renders
/// nothing — the inline chat never appears, and tests that drive it fail at
/// <c>WaitForElement(".input-area")</c>.
///
/// This host supplies the same outlets the layout would, so the page's
/// <c>SectionContent</c> finds a match and the chat (and toolbar) render
/// into the outlet. Tests can then <c>Render&lt;CodedValuesPageHost&gt;()</c>
/// and exercise the page's chat wiring exactly as the running app does.
/// </summary>
internal sealed class CodedValuesPageHost : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // Mirrors SchoolCollabLayout's three slots: pinned toolbar, scroll
        // region (the page's own body), pinned footer. The host does not
        // need the pinned/flex behaviour — only the named-outlet lookup that
        // SectionContent performs — so a flat structure is sufficient.
        builder.OpenComponent<SectionOutlet>(0);
        builder.AddAttribute(1, nameof(SectionOutlet.SectionName), "page-toolbar");
        builder.CloseComponent();

        builder.OpenComponent<IndexPage>(2);
        builder.CloseComponent();

        builder.OpenComponent<SectionOutlet>(3);
        builder.AddAttribute(4, nameof(SectionOutlet.SectionName), "page-footer");
        builder.CloseComponent();
    }
}