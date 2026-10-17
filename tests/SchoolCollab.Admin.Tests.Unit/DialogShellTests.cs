using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the dialog shell introduced by the dialog-container
/// consolidation plan (Phase 1). Covers the
/// <see cref="DialogShellBase{TModel, TResult}"/> contract (AC-1..AC-4), the
/// <see cref="DialogServiceExtensions.ShowShellDialogAsync"/> result-unwrapping
/// contract (AC-5..AC-7), the <see cref="DialogServiceExtensions.BuildShellOptions"/>
/// contract (AC-8), and the <c>@inherits DialogShellBase&lt;TModel,TResult&gt;</c>
/// composition spike (AC-9) — proving the generic-base design compiles and
/// renders inside FluentUI's <c>ShowDialogAsync</c> hosting model.
///
/// <para>Tests use the <em>real</em> <see cref="IDialogService"/> from
/// <c>AddFluentUIComponents</c> plus a rendered <c>FluentDialogProvider</c>,
/// so the full show&rarr;interact&rarr;close&rarr;result path is
/// exercised end-to-end (no FluentUI-internal mocking). JS interop is
/// <c>Loose</c> (the dialog's focus/scroll JS calls are stubbed). Note that
/// FluentUI 5.x's <c>ShowDialogAsync</c> completes its task only when the
/// dialog CLOSES, so a test that wants to observe an open dialog holds the
/// task instead of awaiting it.</para>
/// </summary>
[TestClass]
public class DialogShellTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public DialogShellTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>Renders the dialog provider that hosts the dialogs shown in tests.</summary>
    private IRenderedComponent<FluentDialogProvider> RenderProvider() => Render<FluentDialogProvider>();

    /// <summary>Builds the v5 options for a <see cref="TestShellDialog"/> open: the shell
    /// options (title, width, dismissal posture) plus the <c>Content</c> entry FluentUI 5.x
    /// spreads onto the dialog's typed <c>[Parameter]</c>.</summary>
    private static DialogOptions TestShellOptions(TestShellModel model)
    {
        var options = DialogServiceExtensions.BuildShellOptions("T", DialogWidth.Small);
        options.Parameters[nameof(DialogShellBase<TestShellModel, TestShellResult>.Content)] =
            new DialogShellData<TestShellModel>(model);
        return options;
    }

    // ── AC-1 / AC-6: submit returns non-null → dialog closes with the typed result ──

    [TestMethod]
    public async Task AC1_AC6_Submit_returns_result_closes_dialog_and_extension_unwraps()
    {
        var cut = RenderProvider();
        var model = new TestShellModel
        {
            Behavior = TestSubmitBehavior.ReturnResult,
            ResultValue = "hello",
        };

        // Start the extension (it awaits the dialog result, which won't complete
        // until the form is submitted).
        var task = DialogService.ShowShellDialogAsync<TestShellDialog, TestShellModel, TestShellResult>(
            model, title: "Create thing", size: DialogWidth.Small);

        // Wait for the dialog's EditForm to render inside the provider, then submit it.
        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        var result = await task;
        result.Should().NotBeNull();
        result!.Value.Should().Be("hello");
    }

    // ── AC-4 / AC-5: cancel → Dialog.CancelAsync → extension returns null ──

    [TestMethod]
    public async Task AC4_AC5_Cancel_returns_null_from_extension()
    {
        var cut = RenderProvider();
        var model = new TestShellModel { Behavior = TestSubmitBehavior.ReturnResult };

        var task = DialogService.ShowShellDialogAsync<TestShellDialog, TestShellModel, TestShellResult>(
            model, title: "T");

        // Wait for the dialog to render, then click the Cancel button.
        cut.WaitForAssertion(() => cut.Find("form"));
        var cancelButton = cut
            .FindAll("fluent-button")
            .Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();

        var result = await task;
        result.Should().BeNull();
    }

    // ── AC-2: submit returns null → error shown, dialog stays open ──

    [TestMethod]
    public async Task AC2_Submit_returns_null_shows_error_and_stays_open()
    {
        var cut = RenderProvider();
        var model = new TestShellModel
        {
            Behavior = TestSubmitBehavior.ReturnNullWithError,
            ErrorMessage = "Code and Name are required.",
        };

        var task = DialogService.ShowDialogAsync(typeof(TestShellDialog), TestShellOptions(model));

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        // Error bar appears with the message set by SubmitAsync.
        cut.WaitForAssertion(() =>
        {
            var bar = cut.Find("fluent-message-bar");
            bar.TextContent.Should().Contain("Code and Name are required.");
        });

        // The dialog was NOT closed (CloseAsync is not called on the null-return path).
        task.IsCompleted.Should().BeFalse();
    }

    // ── AC-3: submit throws → exception message shown, dialog stays open ──

    [TestMethod]
    public async Task AC3_Submit_throws_surfaces_message_and_stays_open()
    {
        var cut = RenderProvider();
        var model = new TestShellModel
        {
            Behavior = TestSubmitBehavior.Throw,
            ErrorMessage = "kaboom",
        };

        var task = DialogService.ShowDialogAsync(typeof(TestShellDialog), TestShellOptions(model));

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var bar = cut.Find("fluent-message-bar");
            bar.TextContent.Should().Contain("kaboom");
        });

        task.IsCompleted.Should().BeFalse();
    }

    // ── AC-7: result data is not a DialogShellResult<T> → extension returns null ──

    [TestMethod]
    public async Task AC7_Foreign_result_data_returns_null_from_extension()
    {
        var cut = RenderProvider();
        var model = new TestShellAutoModel { Cancel = false }; // closes with "foreign"

        var result = await DialogService.ShowShellDialogAsync<TestShellDialogAuto, TestShellAutoModel, TestShellResult>(
            model, title: "T");

        result.Should().BeNull();
    }

    // ── AC-9: composition spike — @inherits DialogShellBase<...> renders ──

    [TestMethod]
    public void AC9_Generic_inherits_base_renders_form_and_footer()
    {
        var cut = RenderProvider();
        var model = new TestShellModel { Behavior = TestSubmitBehavior.ReturnResult };

        _ = DialogService.ShowDialogAsync(typeof(TestShellDialog), TestShellOptions(model));

        // The form (from the derived dialog's EditForm) and the submit button
        // (from DialogShellFooter) both render — proving the @inherits
        // composition and the footer child render correctly.
        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("fluent-button")
               .Should().Contain(b => b.TextContent.Contains("Test Submit"));
        });
    }

    // ── AC-8: BuildShellOptions applies the title + width + dismissal posture ──

    [TestMethod]
    public void AC8_BuildShellOptions_applies_title_width_and_dismissal_posture()
    {
        var p = DialogServiceExtensions.BuildShellOptions("Add Attribute Definition", DialogWidth.Medium);

        p.Header.Title.Should().Be("Add Attribute Definition");
        // The header close (X) is switched on here: DialogOptionsHeaderAction.Visible defaults to false,
        // so without it the X would never render even now that the dialogs render DialogShellBody.
        p.Header.CloseAction.Visible.Should().BeTrue();
        p.Width.Should().Be("560px");
        // No FluentUI footer actions: the dialogs own their in-body Cancel/Save row. A label-less
        // action does not render (DialogOptionsFooterAction.ToDisplay), and DialogShellBase clears
        // the localised OK/Cancel labels FluentDialogInstance pre-fills.
        p.Footer.PrimaryAction?.Label.Should().BeNull();
        p.Footer.SecondaryAction?.Label.Should().BeNull();
        // The v4 dismissal posture, re-expressed in v5's vocabulary: Modal = true renders
        // type="alert" (a stray backdrop click is swallowed) and ESC is blocked. An accidental
        // overlay click or ESC key must never discard in-progress form state.
        p.Modal.Should().BeTrue();
        p.PreventDismissOnEscape.Should().BeTrue();
    }

    [TestMethod]
    public void AC8_DialogWidth_maps_each_enum_value_to_its_css_width()
    {
        // ToCssWidth: the canonical mapping every BuildShellOptions call goes through.
        DialogWidth.Small.ToCssWidth().Should().Be("420px");
        DialogWidth.Medium.ToCssWidth().Should().Be("560px");
        DialogWidth.Large.ToCssWidth().Should().Be("720px");
        DialogWidth.ExtraLarge.ToCssWidth().Should().Be("960px");

        // BuildShellOptions defaults to Small (420px) — the size the four
        // CodedValueDialog call sites inherit by passing no size argument.
        var pDefault = DialogServiceExtensions.BuildShellOptions("Override Grade Name");
        pDefault.Width.Should().Be("420px");

        // Each size flows through BuildShellOptions to the same CSS width.
        DialogServiceExtensions.BuildShellOptions("T", DialogWidth.Medium).Width.Should().Be("560px");
        DialogServiceExtensions.BuildShellOptions("T", DialogWidth.Large).Width.Should().Be("720px");
        DialogServiceExtensions.BuildShellOptions("T", DialogWidth.ExtraLarge).Width.Should().Be("960px");
    }

    // ── Q23 (Round A): the shared dialog body restores the v5 dialog chrome ──

    /// <summary>
    /// Regression for the v5 migration defect: v5 removed <c>FluentDialogHeader</c>/
    /// <c>FluentDialogFooter</c> and renders the dialog header — title and close (X) — only through
    /// <c>FluentDialogBody</c>. Nothing in the repo rendered it, so every dialog shipped title-less
    /// and X-less with <b>no compiler error</b>. <see cref="DialogShellBody"/> is the shared wrapper
    /// that fixes the class; this test pins both halves end-to-end, including that the X routes
    /// through the shell's cancel path rather than closing with a success result.
    /// </summary>
    [TestMethod]
    public async Task Q23_DialogShellBody_renders_the_header_title_and_a_close_action_that_cancels()
    {
        var cut = RenderProvider();
        var model = new TestShellModel { Behavior = TestSubmitBehavior.ReturnResult };
        var options = DialogServiceExtensions.BuildShellOptions("Create thing", DialogWidth.Small);
        options.Parameters[nameof(DialogShellBase<TestShellModel, TestShellResult>.Content)] =
            new DialogShellData<TestShellModel>(model);

        var task = DialogService.ShowDialogAsync(typeof(TestShellDialog), options);

        // The dialog body is the only element that renders the header chrome in v5.
        cut.WaitForAssertion(() => cut.Find("fluent-dialog-body"));

        // Title — DialogOptions.Header.Title, rendered into FluentSlot.DialogTitle ("title").
        cut.FindAll("div[slot='title']")
            .Should().ContainSingle()
            .Which.TextContent.Should().Be("Create thing");

        // Close (X) — the header action slot holds the icon-only FluentButton.
        var closeActionSlot = cut.FindAll("div[slot='title-action']").Should().ContainSingle().Which;
        var closeButton = closeActionSlot.QuerySelector("fluent-button");
        closeButton.Should().NotBeNull("the header close affordance is a FluentButton");

        // …and clicking it dismisses through DialogShellBase.OnActionClickedAsync(false) → cancel.
        closeButton!.Click();

        var result = await task;
        result.Cancelled.Should().BeTrue("the X routes through the shell's cancel path");
    }
}
