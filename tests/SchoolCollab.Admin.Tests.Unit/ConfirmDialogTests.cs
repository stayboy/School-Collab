using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the reusable <see cref="ConfirmDialog"/> (shown via
/// <see cref="DialogServiceExtensions.ShowConfirmDialogAsync"/>). Verifies the
/// modal overlay renders and that the dialog can be dismissed by clicking the
/// overlay (outside the dialog) — the two behaviours the user asked to confirm.
/// </summary>
[TestClass]
public class ConfirmDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public ConfirmDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    [TestMethod]
    public async Task ConfirmDialog_RendersModal_WithOverlay()
    {
        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowConfirmDialogAsync("Are you sure?", "Remove", "Cancel", "Remove");

        // The dialog must render as modal (dark overlay obscures the page behind).
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("modal=\"true\""));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Are you sure"));

        // The ConfirmDialog's Primary (accent) and Secondary (outline) buttons render.
        cut.WaitForAssertion(() =>
            cut.FindAll(".confirm-dialog fluent-button[appearance='accent']").Any());
        cut.FindAll(".confirm-dialog fluent-button[appearance='outline']").Should().NotBeEmpty(
            "the Cancel (secondary) button must render");

        // Only the ConfirmDialog's own Remove + Cancel buttons must render — the
        // FluentUI default footer (which would add extra OK/Cancel buttons) is
        // suppressed via PrimaryAction=null / SecondaryAction=null. The header
        // dismiss (Close) button is an icon with no text, so filter it out.
        cut.FindAll("fluent-dialog fluent-button")
            .Select(b => b.TextContent.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Should().BeEquivalentTo(
                new[] { "Cancel", "Remove" },
                "only the ConfirmDialog's own buttons render; the default OK/Cancel footer is suppressed");

        // Dismiss the dialog so the awaited task completes.
        cut.Find(".confirm-dialog fluent-button[appearance='outline']").Click();
        (await task).Should().BeFalse();
    }

    [TestMethod]
    public async Task ConfirmDialog_PrimaryButton_Confirms()
    {
        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowConfirmDialogAsync("Are you sure?", "Remove", "Cancel", "Remove");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("modal=\"true\""));
        cut.WaitForAssertion(() =>
            cut.FindAll(".confirm-dialog fluent-button[appearance='accent']").Any());

        cut.Find(".confirm-dialog fluent-button[appearance='accent']").Click();

        (await task).Should().BeTrue();
    }

    [TestMethod]
    public async Task ConfirmDialog_IsModal_AndDismissibleOnOverlayClick()
    {
        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowConfirmDialogAsync("Are you sure?", "Remove", "Cancel", "Remove");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("modal=\"true\""));
        cut.WaitForAssertion(() => cut.FindAll("fluent-dialog").Any());

        // The dialog must render a modal overlay (dark background) AND be
        // dismissible by clicking the overlay (outside the dialog).
        var dialog = cut.FindComponents<FluentDialog>().First();
        dialog.Instance.Instance.Parameters.Modal.Should().BeTrue(
            "the dialog must render a modal overlay");
        dialog.Instance.Instance.Parameters.PreventDismissOnOverlayClick.Should().BeFalse(
            "clicking the overlay (outside the dialog) must dismiss it");

        // Dismiss via the Cancel button so the awaited task completes.
        cut.Find(".confirm-dialog fluent-button[appearance='outline']").Click();
        (await task).Should().BeFalse();
    }
}
