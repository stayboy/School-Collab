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
/// contract (AC-5..AC-7), the <see cref="DialogServiceExtensions.BuildShellParameters"/>
/// contract (AC-8), and the <c>@inherits DialogShellBase&lt;TModel,TResult&gt;</c>
/// composition spike (AC-9) — proving the generic-base design compiles and
/// renders inside FluentUI's <c>ShowDialogAsync</c> hosting model.
///
/// <para>Tests use the <em>real</em> <see cref="IDialogService"/> from
/// <c>AddFluentUIComponents</c> plus a rendered <c>FluentDialogProvider</c>,
/// so the full show&rarr;interact&rarr;close&rarr;<c>dialog.Result</c> path is
/// exercised end-to-end (no FluentUI-internal mocking). JS interop is
/// <c>Loose</c> (the dialog's focus/scroll JS calls are stubbed).</para>
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

        // Start the extension (it awaits dialog.Result, which won't complete
        // until the form is submitted).
        var task = DialogService.ShowShellDialogAsync<TestShellDialog, TestShellModel, TestShellResult>(
            model, title: "Create thing", size: DialogSize.Small);

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

        var dialog = await DialogService.ShowDialogAsync<TestShellDialog, DialogShellData<TestShellModel>>(
            new DialogShellData<TestShellModel>(model),
            DialogServiceExtensions.BuildShellParameters("T", DialogSize.Small));

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        // Error bar appears with the message set by SubmitAsync.
        cut.WaitForAssertion(() =>
        {
            var bar = cut.Find(".fluent-messagebar");
            bar.TextContent.Should().Contain("Code and Name are required.");
        });

        // The dialog was NOT closed (CloseAsync is not called on the null-return path).
        dialog.Result.IsCompleted.Should().BeFalse();
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

        var dialog = await DialogService.ShowDialogAsync<TestShellDialog, DialogShellData<TestShellModel>>(
            new DialogShellData<TestShellModel>(model),
            DialogServiceExtensions.BuildShellParameters("T", DialogSize.Small));

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var bar = cut.Find(".fluent-messagebar");
            bar.TextContent.Should().Contain("kaboom");
        });

        dialog.Result.IsCompleted.Should().BeFalse();
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

        DialogService.ShowDialogAsync<TestShellDialog, DialogShellData<TestShellModel>>(
            new DialogShellData<TestShellModel>(model),
            DialogServiceExtensions.BuildShellParameters("T", DialogSize.Small)).Wait();

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

    // ── AC-8: BuildShellParameters applies the four constants + title + width ──

    [TestMethod]
    public void AC8_BuildShellParameters_applies_title_width_and_constants()
    {
        var p = DialogServiceExtensions.BuildShellParameters("Add Attribute Definition", DialogSize.Medium);

        p.Title.Should().Be("Add Attribute Definition");
        p.Width.Should().Be("560px");
        p.PrimaryAction.Should().BeNull();
        p.SecondaryAction.Should().BeNull();
        p.PreventDismissOnOverlayClick.Should().BeTrue();
    }

    [TestMethod]
    public void AC8_DialogSize_maps_each_enum_value_to_its_css_width()
    {
        // ToCssWidth: the canonical mapping every BuildShellParameters call goes through.
        DialogSize.Small.ToCssWidth().Should().Be("420px");
        DialogSize.Medium.ToCssWidth().Should().Be("560px");
        DialogSize.Large.ToCssWidth().Should().Be("720px");
        DialogSize.ExtraLarge.ToCssWidth().Should().Be("960px");

        // BuildShellParameters defaults to Small (420px) — the size the four
        // CodedValueDialog call sites inherit by passing no size argument.
        var pDefault = DialogServiceExtensions.BuildShellParameters("Override Grade Name");
        pDefault.Width.Should().Be("420px");

        // Each size flows through BuildShellParameters to the same CSS width.
        DialogServiceExtensions.BuildShellParameters("T", DialogSize.Medium).Width.Should().Be("560px");
        DialogServiceExtensions.BuildShellParameters("T", DialogSize.Large).Width.Should().Be("720px");
        DialogServiceExtensions.BuildShellParameters("T", DialogSize.ExtraLarge).Width.Should().Be("960px");
    }
}
