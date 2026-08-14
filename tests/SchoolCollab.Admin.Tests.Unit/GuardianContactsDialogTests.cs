using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the read-only
/// <c>GuardianContactsDialog.razor</c> component (spec 2026-07-27 §4.5).
/// The dialog is opened from the student view page's guardians grid when
/// the user clicks "View all (N) contacts" on a row whose
/// <c>TotalContactCount &gt; 3</c>; it loads every contact for one
/// guardian via <c>IContactsClient.ListContactsAsync(Guardian, id)</c>
/// and renders them in display order with verified badges.
///
/// Note: this dialog is intentionally NOT a <c>DialogShellBase</c> (no
/// model, no OK/Cancel result, no form). It is a plain
/// <c>ComponentBase</c> hosting the full-contacts read-only display, and
/// it closes itself by calling <c>Dialog.CloseAsync()</c> on the
/// cascading <c>FluentDialog</c> reference provided by the dialog host.
/// </summary>
[TestClass]
public class GuardianContactsDialogTests
{
    private const string ComponentPath = "GuardianContactsDialog.razor";
    private const string CssPath = "GuardianContactsDialog.razor.css";

    [TestMethod]
    public void Component_IsPlain_ComponentBase_NotADialogShell()
    {
        var razor = ReadSource(ComponentPath);

        // Read-only dialog: no model, no result, no OK/Cancel. It must NOT
        // inherit DialogShellBase (which would require a model + result
        // and enforce the form-dialog UX). The host opens it via the
        // generic IDialogService.ShowDialogAsync<T>(parameters) overload
        // planned in step 4.
        razor.Should().NotContain("@inherits DialogShellBase",
            "the dialog is read-only — it must not be a DialogShellBase form");
        razor.Should().NotContain("DialogShellData",
            "the dialog has no typed Content.Data model");
    }

    [TestMethod]
    public void Component_Declares_RequiredGuardianId_And_OptionalTitleAndSubtitle()
    {
        var razor = ReadSource(ComponentPath);

        // FluentUI 4.14.x does NOT spread DialogParameters indexer entries onto
        // separate [Parameter]s, so the dialog reads its inputs from Content.
        razor.Should().Contain("public const string GuardianIdKey",
            "GuardianId is read from Content (the DialogParameters indexer)");
        razor.Should().Contain("TryGet<Guid>(GuardianIdKey)",
            "GuardianId is the required input that drives the contact load");
        razor.Should().Contain("public const string GuardianNameKey",
            "GuardianName is read from Content (used for the dialog title)");
        razor.Should().Contain("public const string SubtitleKey",
            "Subtitle is read from Content (the optional secondary line)");
    }

    [TestMethod]
    public void Component_Injects_IContactsClient_AndLoadsGuardianContacts_OnParametersSetAsync()
    {
        var razor = ReadSource(ComponentPath);

        razor.Should().Contain("@inject IContactsClient ContactsApi",
            "the dialog depends on the contract, not the concrete API client");
        razor.Should().Contain("ListContactsAsync(ContactOwnerType.Guardian, GuardianId",
            "OnParametersSetAsync loads via ListContactsAsync for the Guardian owner type, scoped to the row's GuardianId");
    }

    [TestMethod]
    public void Component_Orders_ContactsByDisplayOrder_ThenCreatedAt()
    {
        var razor = ReadSource(ComponentPath);

        // DisplayOrder ascending; CreatedAt as a stable tiebreaker so the
        // ordering is deterministic when two contacts share an order. The
        // API also returns ordered, but we sort client-side defensively.
        razor.Should().Contain(".OrderBy(c => c.DisplayOrder)",
            "primary sort is by DisplayOrder ascending (lowest = preferred)");
        razor.Should().Contain(".ThenBy(c => c.CreatedAt)",
            "tiebreaker is CreatedAt for stable rendering");
    }

    [TestMethod]
    public void Component_Renders_OneRowPerContact_WithVerifiedBadge_WhenIsVerified()
    {
        var razor = ReadSource(ComponentPath);

        // Row markup: channel glyph + name, formatted value ([+CC] value),
        // optional @Label, and a Verified badge when IsVerified (and
        // Unverified otherwise, mirroring the live list UX).
        razor.Should().Contain("@foreach (var c in _contacts)",
            "the body iterates the loaded contacts");
        razor.Should().Contain("c.DisplayOrder == 0",
            "the first contact (lowest DisplayOrder) is flagged primary (--primary class)");
        razor.Should().Contain("c.IsVerified",
            "each contact's IsVerified flag drives the Verified/Unverified badge");
        razor.Should().Contain("\">Verified</FluentBadge>",
            "verified contacts render a Verified FluentBadge");
        razor.Should().Contain("\">Unverified</FluentBadge>",
            "unverified contacts render an Unverified FluentBadge");
    }

    [TestMethod]
    public void Component_Renders_EmptyState_When_ApiReturns_NullOrEmpty()
    {
        var razor = ReadSource(ComponentPath);

        razor.Should().Contain("No contacts on file for this guardian.",
            "empty-state FluentMessageBar when the API returns null or zero contacts");
    }

    [TestMethod]
    public void Component_Has_Cancellation_CTS_And_Disposed_Flag()
    {
        var razor = ReadSource(ComponentPath);

        // Mirrors the pattern in ContactsEditor / GuardianContactsList so
        // rapid re-opens and disposes are safe.
        razor.Should().Contain("CancellationTokenSource? _loadCts",
            "a single CTS guards the in-flight load");
        razor.Should().Contain("private volatile bool _disposed",
            "a _disposed flag prevents state writes after Dispose");
        razor.Should().Contain("implements IDisposable",
            "the component implements IDisposable");
        razor.Should().Contain("public void Dispose()",
            "Dispose cancels and disposes the CTS");
    }

    [TestMethod]
    public void Component_Closes_Via_Cascading_FluentDialog_And_ReopensOnGuardianIdChange()
    {
        var razor = ReadSource(ComponentPath);

        // The dialog host (FluentUI) provides its FluentDialog reference
        // via a cascading parameter; the footer Close button calls
        // Dialog.CloseAsync(). OnParametersSetAsync reloads whenever
        // GuardianId changes so the same component instance can be
        // reused for a different guardian.
        razor.Should().Contain("[CascadingParameter] public FluentDialog? Dialog",
            "the FluentDialog reference comes from a cascading parameter (not injected)");
        razor.Should().Contain("Dialog.CloseAsync()",
            "the footer Close button dismisses the host FluentDialog");
        razor.Should().Contain("if (_loadedFor == GuardianId) return",
            "OnParametersSetAsync reloads only when GuardianId changes");
    }

    [TestMethod]
    public void Css_Defines_FooterSeparator_AndPrimaryAnchorRowStyles()
    {
        var css = ReadSource(CssPath);

        // The dialog has no PrimaryAction/SecondaryAction (the host sets
        // both null in BuildShellParameters), so the body owns the close
        // affordance. A top border separates content from footer actions
        // (the dialog-ui skill's content/actions split).
        css.Should().Contain(".guardian-contacts-footer",
            "footer block hosts the Close button");
        css.Should().Contain("border-top: 1px solid var(--neutral-stroke-rest",
            "footer is separated from content by a top border");
        css.Should().Contain(".guardian-contact-row--primary",
            "the preferred contact row is visually anchored");
    }

    private static string ReadSource(string relativePath)
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Application",
            "Components", "Students", relativePath));
        File.Exists(srcPath).Should().BeTrue(
            $"{relativePath} should exist at '{srcPath}' — check the path resolution");
        return File.ReadAllText(srcPath);
    }
}
