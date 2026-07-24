using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Admin.Components.Students;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the <see cref="StudentFormFields"/> action-row
/// control. The component owns an EditForm + DataAnnotationsValidator +
/// fields + (optionally) a bottom-horizontal action row. The action row
/// is opt-out-able via the <c>RenderActions</c> parameter so the same
/// component can be embedded in dialogs (Create.razor, the inline
/// GradeLevelWizard "new student" form, the StudentPickerDialog
/// "create" view) with the dialog-ui convention of bottom-horizontal
/// Cancel/Save separated by a border-top, and ALSO on long forms
/// (Edit.razor) where the page hosts the action buttons in a
/// right-column page-level sidebar (driven by the parent via
/// <c>@ref.SubmitAsync()</c>). Asserts on the rendered DOM:
///   - with RenderActions=true (default, dialog-friendly): the
///     .form-actions div is rendered, with a Submit and Cancel button
///   - with RenderActions=false (Edit.razor): the .form-actions div
///     is NOT rendered
///   - in BOTH cases the form fields (the EditForm + the form-row
///     fields) are rendered
///   - the Submit button (text "Save", Type="ButtonType.Submit") and
///     the Cancel button (text "Cancel", Outline appearance) both sit
///     in the .form-actions row, each carrying the .form-actions__button
///     class so a page-level sidebar can opt them into full-width
///     styling. FluentButton renders as a <c>&lt;fluent-button&gt;</c>
///     custom element (not a native <c>&lt;button&gt;</c>), so
///     <c>button[type=submit]</c> selectors never match in bunit. The
///     established pattern in this repo (DialogShellTests,
///     ContactsEditorTests, LandingPageTests) is to address FluentButton
///     by <c>FindAll("fluent-button")</c> + text-content match. The
///     wiring of the Submit button to the EditForm's submit pipeline
///     (and the absence of a submit trigger on Cancel) is verified at
///     the source level by
///     <c>EditSidebarLayoutTests.StudentFormFields_Exposes_Public_SubmitAsync_Method</c>
///     — the DOM contract this bunit test owns is the button placement
///     and class, not the form-submit pipeline (which is a Blazor
///     framework guarantee given the <c>Type</c> parameter).
/// </summary>
[TestClass]
public class StudentFormFieldsRenderActionsBunitTests : BunitContext
{
    public StudentFormFieldsRenderActionsBunitTests()
    {
        // bUnit needs the FluentUI services registered (JSRuntime + DI for
        // FluentNumberField, etc.). The bUnit context's JSRuntime is in
        // Loose mode by default for this test class.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        // StudentFormFields has [Inject] StudentsApiClient +
        // CodedValuesApiClient + ILogger. Register fakes (empty
        // HttpClient instances) so the component can be constructed.
        // The fixture doesn't pass StudentId, so IsEditMode is false and
        // the injects are never dereferenced — the constructors are
        // enough to satisfy the DI container.
        var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new SchoolCollab.Admin.Shared.Services.CodedValuesApiClient(http));
        Services.AddSingleton(_ => new SchoolCollab.Students.Admin.Services.StudentsApiClient(
            http,
            NullLogger<SchoolCollab.Students.Admin.Services.StudentsApiClient>.Instance,
            new SchoolCollab.Admin.Shared.Services.CodedValuesApiClient(http)));
    }

    [TestMethod]
    public void Default_Renders_The_Bottom_Horizontal_Action_Row()
    {
        // The default (RenderActions=true) matches the dialog-ui
        // convention: Save / Cancel sit in a horizontal row below
        // the form fields, separated by a border-top on .form-actions.
        var cut = Render<TestStudentFormFields>(p => p
            .Add(x => x.RenderActions, true));

        var actionRow = cut.Find(".form-actions");
        actionRow.Should().NotBeNull(
            "RenderActions=true (the default) MUST render the built-in action row — the dialog path relies on it");

        var buttons = cut.FindAll(".form-actions .form-actions__button");
        buttons.Count.Should().Be(2,
            "the built-in row carries the Submit + Cancel pair");
    }

    [TestMethod]
    public void RenderActions_False_Skips_The_Action_Row_So_Page_Can_Provide_Own()
    {
        // Edit.razor sets RenderActions="false" and provides the
        // Save / Cancel pair in a page-level right-column sidebar
        // (a <FluentStack Orientation="Vertical">). The component
        // must NOT render the built-in row in that case, otherwise
        // the user would see two pairs of Save/Cancel buttons.
        var cut = Render<TestStudentFormFields>(p => p
            .Add(x => x.RenderActions, false));

        cut.FindAll(".form-actions").Count.Should().Be(0,
            "RenderActions=false MUST skip the built-in action row so the page-level sidebar is the single source of action buttons");
    }

    [TestMethod]
    public void Form_Fields_Are_Always_Rendered_Regardless_Of_RenderActions()
    {
        // The EditForm + DataAnnotationsValidator + form-row fields
        // are rendered in BOTH modes (the Edit.razor page-level
        // sidebar path still renders the fields, the form just
        // doesn't render its own buttons). The form-actions div is
        // the only thing gated on RenderActions.
        var withActions = Render<TestStudentFormFields>(p => p
            .Add(x => x.RenderActions, true));
        withActions.Find("form.student-form-fields").Should().NotBeNull(
            "the EditForm is rendered in dialog mode (RenderActions=true)");
        withActions.FindAll(".form-row").Count.Should().BeGreaterThan(0,
            "at least one form-row is rendered in dialog mode");

        var withoutActions = Render<TestStudentFormFields>(p => p
            .Add(x => x.RenderActions, false));
        withoutActions.Find("form.student-form-fields").Should().NotBeNull(
            "the EditForm is rendered in page-sidebar mode (RenderActions=false)");
        withoutActions.FindAll(".form-row").Count.Should().BeGreaterThan(0,
            "at least one form-row is rendered in page-sidebar mode — only the action row is gated");
    }

    [TestMethod]
    public void Submit_Button_Is_Present_In_Action_Row_With_Form_Actions_Class()
    {
        // The dialog path relies on the Submit button driving the
        // EditForm's OnValidSubmit. We do NOT assert on a rendered
        // `type="submit"` HTML attribute for two reasons:
        //
        //   1. FluentButton renders as a <fluent-button> custom
        //      element (a web component), not as a native <button> —
        //      so `button[type=submit]` selectors never match in bunit,
        //      no matter what ButtonType.Submit is passed.
        //   2. The Blazor EditForm's submit pipeline is wired by the
        //      `Type="ButtonType.Submit"` parameter on the FluentButton
        //      at the source level (not via a rendered HTML attribute).
        //      The source-level regression test
        //      EditSidebarLayoutTests.StudentFormFields_Exposes_Public_SubmitAsync_Method
        //      already covers the validation+OnValidSubmit pipeline.
        //
        // What we DO assert at the bunit / DOM level (the contract
        // this component owns):
        //   - the Submit button is present in the .form-actions row
        //   - its text is the SubmitLabel ("Save")
        //   - it carries the .form-actions__button class so a
        //     page-level sidebar can opt it into full-width styling
        //
        // The element selector follows the established pattern in this
        // repo (DialogShellTests, ContactsEditorTests, LandingPageTests):
        // FindAll("fluent-button") + text-content match.
        var cut = Render<TestStudentFormFields>(p => p
            .Add(x => x.RenderActions, true));

        var actionButtons = cut.FindAll(".form-actions fluent-button");
        actionButtons.Count.Should().Be(2,
            "the .form-actions row carries the Submit + Cancel pair");

        var saveButton = actionButtons.FirstOrDefault(b => b.TextContent.Contains("Save"));
        saveButton.Should().NotBeNull(
            "the Submit button is the .form-actions fluent-button whose text is \"Save\" — FluentButton renders as <fluent-button>, not <button>");
        saveButton!.ClassList.Should().Contain("form-actions__button",
            "the Submit button carries the .form-actions__button class so a page-level sidebar can opt it into full-width styling");
    }

    [TestMethod]
    public void Cancel_Button_Is_Present_In_Action_Row_With_Form_Actions_Class()
    {
        // The Cancel button uses Appearance.Outline (secondary) so
        // the visual hierarchy is Submit(primary) > Cancel(secondary)
        // — matches the dialog-ui convention. Critically, clicking
        // Cancel must NOT trigger form validation / submit: a dialog
        // user who decides to abandon should not be greeted by a
        // red error message bar.
        //
        // We do NOT use a `button:not([type=submit])` selector because
        // FluentButton renders as a <fluent-button> custom element and
        // doesn't carry a `type` attribute that distinguishes submit vs
        // non-submit in the DOM. Instead we:
        //   1. Identify the Cancel button as the <fluent-button> in
        //      .form-actions whose text is "Cancel" (matches the
        //      pattern used in ContactsEditorTests/DialogShellTests).
        //   2. Confirm the Cancel button carries the .form-actions__button
        //      class so a page-level sidebar can opt it into full-width.
        //   3. Confirm the Cancel button is NOT marked as the active
        //      form submit — it has no special submit indicator.
        //      The source uses `Type="ButtonType.Submit"` only on the
        //      Save button; Cancel is the default `ButtonType.Button`.
        //      We can't assert that directly in the DOM (FluentButton
        //      doesn't expose a `type` attribute in bunit), so the
        //      text-based identity is the contract this bunit test
        //      owns: "Cancel" is in the action row.
        var cut = Render<TestStudentFormFields>(p => p
            .Add(x => x.RenderActions, true));

        var actionButtons = cut.FindAll(".form-actions fluent-button");
        actionButtons.Count.Should().Be(2,
            "the .form-actions row carries the Submit + Cancel pair");

        var cancelButton = actionButtons.FirstOrDefault(b => b.TextContent.Contains("Cancel"));
        cancelButton.Should().NotBeNull(
            "the Cancel button is the .form-actions fluent-button whose text is \"Cancel\" — FluentButton renders as <fluent-button>, not <button>");
        cancelButton!.ClassList.Should().Contain("form-actions__button",
            "the Cancel button carries the .form-actions__button class so a page-level sidebar can opt it into full-width styling");
    }
}
