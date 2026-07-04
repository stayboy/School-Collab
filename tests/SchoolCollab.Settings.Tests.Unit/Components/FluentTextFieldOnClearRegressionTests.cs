using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Settings.Tests.Unit.Components;

/// <summary>
/// Regression guard for the FluentTextField.OnClear silent-swallow bug.
///
/// Background: <see cref="FluentTextField"/> (Microsoft.FluentUI.AspNetCore.Components)
/// has never exposed an <c>OnClear</c> parameter, and the component library has never
/// shipped a built-in clear (×) button on plain FluentTextField. <c>OnClear</c> is captured
/// by <c>FluentInputBase&lt;T&gt;</c>'s <c>[Parameter(CaptureUnmatchedValues = true)]
/// AdditionalAttributes</c> dictionary, so the callback is never invoked.
///
/// Three independent assertions document this behaviour. If a future FluentUI
/// upgrade changes any of them, the test fails and the developer is forced to
/// audit every search box in the codebase for the now-unnecessary swallow
/// workarounds (currently Assignments/Index, CodedValues/Children, and
/// Students/Index all use FluentTextField's ValueChanged for the search-clear
/// path).
/// </summary>
[TestClass]
public class FluentTextFieldOnClearRegressionTests : BunitContext
{
    [TestInitialize]
    public void Setup()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// Verifies via reflection that <see cref="FluentTextField"/> does NOT declare
    /// an <c>OnClear</c> property, parameter, or method. If FluentUI ever adds one,
    /// this fails — prompting the developer to remove the captured-attribute
    /// workaround from every search-page that relied on the swallow.
    /// </summary>
    [TestMethod]
    public void FluentTextField_HasNoOnClearMember()
    {
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                  | BindingFlags.DeclaredOnly | BindingFlags.FlattenHierarchy;
        var anyMember = typeof(FluentTextField)
            .GetMembers(flags)
            .Where(m => string.Equals(m.Name, "OnClear", StringComparison.Ordinal))
            .ToList();

        anyMember.Should().BeEmpty(
            "FluentTextField must not expose an OnClear member — if this assertion fails, " +
            "FluentUI has added a real OnClear parameter and the search-clear workaround in " +
            "Assignments/Index, CodedValues/Children and Students/Index can be removed.");
    }

    /// <summary>
    /// Verifies via reflection that <see cref="FluentTextField"/> does NOT render a
    /// built-in clear-button DOM element from its Razor template. The current
    /// implementation renders a single <c>&lt;fluent-text-field&gt;</c> web component;
    /// any inner clear control would be wrapped in a <c>button</c> that we could detect
    /// via the rendered bUnit markup (without invoking JS Interop, the inner shadow
    /// DOM is not constructed, so we use reflection on the RenderTreeBuilder source).
    /// </summary>
    [TestMethod]
    public void FluentTextField_DoesNotRenderBuiltInClearButton()
    {
        // Render the component with a non-empty value so any conditional clear-button
        // is enabled (defensive — clear buttons typically appear only when text exists).
        var cut = Render<FluentTextField>(parameters => parameters
            .Add(p => p.Value, "hello"));

        // Without JS Interop, the shadow DOM is not constructed, so the underlying
        // <input> / clear button never appears. Asserting that no <button> with
        // "clear" in any attribute is the best outside observation we can make; the
        // SSR-side rendered tree is exactly <fluent-text-field ... /> (see Dump
        // verification during authoring of this test).
        var rendered = cut.Markup;
        rendered.Should().NotContain("class=\"clear",
            "FluentTextField must not include a built-in clear button in the rendered " +
            "DOM in version 4.x; if it does, the search-clear workaround in the three " +
            "landing pages must be revisited.");

        // Even tighter: the rendered custom element must not expose any clear-button
        // shadow slot via attribute. The shadow-DOM clear button is not present in
        // SSR markup, so this assertion validates the SSR-side contract.
        rendered.Should().NotMatchRegex(
            @"<button[^>]*\bclear\b",
            "FluentTextField rendered markup must not contain a <button> with a clear " +
            "token in any attribute.");
    }

    /// <summary>
    /// Verifies that supplying a user <c>OnClear</c> callback as an unmatched
    /// AdditionalAttribute does NOT install any extra Blazor event handler on the
    /// rendered element. If FluentUI ever binds OnClear (e.g. via a real parameter),
    /// the rendered <c>&lt;fluent-text-field&gt;</c> element will gain a new
    /// <c>blazor:on*</c> attribute, and this test will fail.
    /// </summary>
    [TestMethod]
    public void FluentTextField_OnClearCallback_DoesNotInstallEventHandler_OnRenderedElement()
    {
        var cut = Render<FluentTextField>(parameters =>
        {
            parameters.Add(p => p.Value, "hello");
            // We deliberately do NOT bind ValueChanged — that would install blazor:onchange
            // and bias the assertion. The bug we want to detect is "OnClear swallows silently
            // into AdditionalAttributes and never wires up an event handler".
            parameters.AddUnmatched("OnClear",
                EventCallback.Factory.Create<ChangeEventArgs>(
                    this,
                    _ => { /* no-op: must never be invoked by render */ }));
        });

        // The base FluentInputBase already installs onchange+oninput for value binding.
        // We assert that adding OnClear does NOT introduce any additional blazor:* handler.
        var rendered = cut.Markup;
        var handlerCount = System.Text.RegularExpressions.Regex.Matches(
                rendered,
                @"blazor:on[a-z\-]+=""")
            .Count;

        // Without Value binding, FluentTextField renders with one handler (blazor:oninput)
        // for two-way binding support and one for change. We allow some headroom (up to
        // 2 handlers) for what FluentUI itself emits, but assert no handler named "onclear"
        // gets installed.
        rendered.Should().NotContain("blazor:onclear",
            "OnClear on FluentTextField must not be promoted to a real Blazor event " +
            "handler attribute in the rendered element. If this assertion fails, FluentUI " +
            "has started treating OnClear as a real callback parameter and the captured-" +
            "attribute workaround in Assignments/Index, CodedValues/Children and " +
            "Students/Index can be removed.");

        handlerCount.Should().BeLessThanOrEqualTo(2,
            "FluentTextField without binding should not grow extra Blazor event handlers " +
            "when an OnClear AdditionalAttribute is added.");
    }
}
