using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Shared.Components;

namespace SchoolCollab.CodedValues.Tests.Unit.Components;

/// <summary>
/// bUnit tests for the shared <see cref="SideDrawer"/> component's
/// open / close / re-open lifecycle.
///
/// Regression coverage for the bug where the drawer opened on the first
/// <c>Open=true</c>, but failed to reopen on a subsequent <c>Open=true</c>
/// after the user had closed it (e.g. via the × button, Cancel button, or
/// backdrop click).
///
/// In bUnit 2.7.2 the only supported way to drive parameter changes on
/// an existing rendered component is to use a small wrapper component
/// (here, <see cref="DrawerHost"/>) that owns the parameter as a public
/// <c>[Parameter]</c> property and re-renders. The wrapper pattern mirrors
/// what real host pages do with <c>@bind-Open</c>: the page owns the
/// state, the drawer reacts. Mutating <c>host.Open</c> + calling
/// <c>StateHasChanged</c> propagates the new value to the drawer.
/// </summary>
[TestClass]
public class SideDrawerTests : BunitContext
{
    [TestInitialize]
    public void Setup()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// Hosts a <see cref="SideDrawer"/> with two-way bound <c>Open</c>.
    /// <see cref="Open"/> is declared with <c>[Parameter]</c> so bUnit's
    /// parameter builder can validate it. The two-way binding is closed
    /// properly: <see cref="OpenChanged"/> updates <see cref="Open"/>
    /// back on the host when the drawer requests a close, mirroring how
    /// <c>@bind-Open</c> works on a real page. Without this round-trip the
    /// drawer would receive <c>Open=true</c> again on the next render and
    /// silently stay open.
    /// </summary>
    private sealed class DrawerHost : ComponentBase
    {
        [Parameter] public bool Open { get; set; }
        [Parameter] public bool ShowCancel { get; set; }
        [Parameter] public string CancelText { get; set; } = "Cancel";

        /// <summary>Mirrors <see cref="SideDrawer.OpenChanged"/>.</summary>
        [Parameter] public EventCallback<bool> OpenChanged { get; set; }

        public void SetOpen(bool value)
        {
            Open = value;
            StateHasChanged();
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SideDrawer>(0);
            builder.AddAttribute(1, "Title", "Test drawer");
            builder.AddAttribute(2, "Open", Open);
            builder.AddAttribute(3, "OpenChanged",
                EventCallback.Factory.Create<bool>(this, OnDrawerOpenChanged));
            if (ShowCancel)
            {
                builder.AddAttribute(4, "ShowCancel", true);
                builder.AddAttribute(5, "CancelText", CancelText);
            }
            builder.AddAttribute(6, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenElement(7, "p");
                b.AddContent(8, "Body");
                b.CloseElement();
            }));
            builder.CloseComponent();
        }

        // Closes the two-way binding: when the drawer requests a close,
        // mirror the new value into Open and re-render. Do NOT invoke
        // OpenChanged here — that would re-enter the drawer.
        private void OnDrawerOpenChanged(bool value)
        {
            Open = value;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Helper: render the host with an initial <paramref name="open"/>
    /// value.
    /// </summary>
    private IRenderedComponent<DrawerHost> RenderHost(
        bool open, bool showCancel)
    {
        return Render<DrawerHost>(parameters => parameters
            .Add(p => p.Open, open)
            .Add(p => p.ShowCancel, showCancel)
            .Add(p => p.CancelText, "Close"));
    }

    /// <summary>
    /// The reported bug: first open works, close works, second open does
    /// not. Reproduces by mutating the host's <c>Open</c> flag after the
    /// user dismisses the drawer via the × button.
    /// </summary>
    [TestMethod]
    public void SideDrawer_Reopens_AfterDismissButtonClose()
    {
        var cut = RenderHost(open: true, showCancel: false);
        var host = cut.Instance;

        // 1st open — panel visible.
        cut.Find("aside.side-drawer-panel").Should().NotBeNull();

        // User clicks × — drawer should disappear and host.Open should flip to false.
        cut.Find("button[aria-label='Close drawer']").Click();
        cut.WaitForState(() => host.Open == false);
        host.Open.Should().BeFalse();
        cut.FindAll("aside.side-drawer-panel").Should().BeEmpty();

        // Page fires a 2nd prompt — host.Open goes back to true.
        cut.InvokeAsync(() => host.SetOpen(true));

        cut.FindAll("aside.side-drawer-panel").Should().NotBeEmpty(
            "after re-opening, the drawer must be visible again");
    }

    /// <summary>
    /// Same lifecycle but exercised through the footer Cancel button
    /// rather than the × dismiss button.
    /// </summary>
    [TestMethod]
    public void SideDrawer_Reopens_AfterCancelButtonClose()
    {
        var cut = RenderHost(open: true, showCancel: true);
        var host = cut.Instance;

        cut.Find("aside.side-drawer-panel").Should().NotBeNull();

        cut.Find("button.side-drawer-btn-cancel").Click();
        cut.WaitForState(() => host.Open == false);
        cut.FindAll("aside.side-drawer-panel").Should().BeEmpty();

        cut.InvokeAsync(() => host.SetOpen(true));
        cut.FindAll("aside.side-drawer-panel").Should().NotBeEmpty();
    }

    /// <summary>
    /// Same lifecycle but closed via backdrop click instead of a button.
    /// </summary>
    [TestMethod]
    public void SideDrawer_Reopens_AfterBackdropClose()
    {
        var cut = RenderHost(open: true, showCancel: false);
        var host = cut.Instance;

        cut.Find(".side-drawer-backdrop").Click();
        cut.WaitForState(() => host.Open == false);
        cut.FindAll("aside.side-drawer-panel").Should().BeEmpty();

        cut.InvokeAsync(() => host.SetOpen(true));
        cut.FindAll("aside.side-drawer-panel").Should().NotBeEmpty();
    }

    /// <summary>
    /// Three open / close cycles in a row — exercises the re-entrant
    /// scenario where the user sends three prompts in a row, each preceded
    /// by closing the drawer.
    /// </summary>
    [TestMethod]
    public void SideDrawer_OpensReliably_AcrossThreeCycles()
    {
        var cut = RenderHost(open: false, showCancel: false);
        var host = cut.Instance;

        for (int i = 1; i <= 3; i++)
        {
            cut.InvokeAsync(() => host.SetOpen(true));
            cut.FindAll("aside.side-drawer-panel").Should().NotBeEmpty(
                $"cycle {i}: drawer must open");

            cut.Find("button[aria-label='Close drawer']").Click();
            cut.WaitForState(() => host.Open == false);
            cut.FindAll("aside.side-drawer-panel").Should().BeEmpty(
                $"cycle {i}: drawer must close");
        }
    }

    /// <summary>
    /// Edge case: drawer is open and the parent fires OpenChanged(false)
    /// twice in a row (e.g. duplicate event from rapid clicks). With the
    /// original transitional state machine, the second event arrives as
    /// <c>Open=false &amp;&amp; _open=false</c> — both branches no-op — so
    /// this still works. With the always-sync state machine, redundant
    /// events are harmless.
    /// </summary>
    [TestMethod]
    public void SideDrawer_HandlesRedundantOpenChangedEvents()
    {
        var cut = RenderHost(open: true, showCancel: false);
        var host = cut.Instance;

        // Simulate two redundant close callbacks (the drawer's _open
        // already matches Open=false after the first one).
        cut.Find("button[aria-label='Close drawer']").Click();
        cut.WaitForState(() => host.Open == false);

        // Manually invoke the host's OpenChanged handler again with the
        // same value — the drawer should remain closed.
        cut.InvokeAsync(() => host.Open = false);

        cut.FindAll("aside.side-drawer-panel").Should().BeEmpty();
    }
}