using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Admin.Components.Students;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the <see cref="StudentFormFields"/> actions-placement
/// feature. Asserts on the rendered DOM rather than the source text:
///   - in <c>Bottom</c> mode the form does NOT carry the
///     <c>student-form-fields--sidebar</c> class, and the action row
///     does NOT carry <c>form-actions--sidebar</c>
///   - in <c>Right</c> mode the form carries the sidebar class, the
///     action row carries the sidebar class, and the layout wrapper
///     switches to a CSS Grid (asserted via the parent class chain)
///   - the layout wrapper is ALWAYS present (the markup is a single
///     shape across both placements)
///   - the buttons carry the <c>form-actions__button</c> class so the
///     sidebar CSS can make them full-width
/// </summary>
[TestClass]
public class StudentFormFieldsActionsPlacementBunitTests : BunitContext
{
    public StudentFormFieldsActionsPlacementBunitTests()
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
    public void Bottom_Placement_Does_Not_Add_Sidebar_Class_To_Form_Or_Action_Row()
    {
        var cut = Render<TestStudentFormFields>(p => p
            .Add(x => x.Placement, StudentFormFields.StudentFormActionsPlacement.Bottom));

        var form = cut.Find("form.student-form-fields");
        form.ClassList.Should().NotContain("student-form-fields--sidebar",
            "the bottom placement must NOT carry the sidebar modifier class");

        var actionRow = cut.Find(".form-actions");
        actionRow.ClassList.Should().NotContain("form-actions--sidebar",
            "the action row must NOT carry the sidebar modifier class in bottom placement");
    }

    [TestMethod]
    public void Right_Placement_Adds_Sidebar_Class_To_Form_And_Action_Row()
    {
        var cut = Render<TestStudentFormFields>(p => p
            .Add(x => x.Placement, StudentFormFields.StudentFormActionsPlacement.Right));

        var form = cut.Find("form.student-form-fields");
        form.ClassList.Should().Contain("student-form-fields--sidebar",
            "the right placement MUST carry the sidebar modifier class so the CSS grid kicks in");

        var actionRow = cut.Find(".form-actions");
        actionRow.ClassList.Should().Contain("form-actions--sidebar",
            "the action row must carry the sidebar modifier class in right placement so the buttons stack vertically");
    }

    [TestMethod]
    public void Layout_Wrapper_Is_Always_Rendered_In_Both_Placements()
    {
        // The wrapper is part of the single-shape markup and is rendered
        // in both Bottom and Right modes. The CSS controls whether the
        // wrapper is `display: contents` (Bottom, no extra layout) or
        // `display: grid` (Right, two-column layout).
        var bottom = Render<TestStudentFormFields>(p => p
            .Add(x => x.Placement, StudentFormFields.StudentFormActionsPlacement.Bottom));
        bottom.FindAll(".student-form-fields__layout").Count.Should().Be(1,
            "the layout wrapper is always rendered (Bottom placement)");
        bottom.FindAll(".student-form-fields__fields").Count.Should().Be(1,
            "the fields slot is always rendered (Bottom placement)");
        bottom.FindAll(".student-form-fields__sidebar").Count.Should().Be(1,
            "the sidebar slot is always rendered (Bottom placement)");

        var right = Render<TestStudentFormFields>(p => p
            .Add(x => x.Placement, StudentFormFields.StudentFormActionsPlacement.Right));
        right.FindAll(".student-form-fields__layout").Count.Should().Be(1,
            "the layout wrapper is always rendered (Right placement)");
        right.FindAll(".student-form-fields__fields").Count.Should().Be(1,
            "the fields slot is always rendered (Right placement)");
        right.FindAll(".student-form-fields__sidebar").Count.Should().Be(1,
            "the sidebar slot is always rendered (Right placement)");
    }

    [TestMethod]
    public void Buttons_Carry_form_actions_button_Class_So_CSS_Can_Target_Them()
    {
        // The .form-actions__button class is on the built-in Cancel + Save
        // buttons. The sidebar CSS uses it to make the buttons full-width
        // (width: 100%). The class is always present regardless of
        // placement so the CSS rule applies in both modes.
        var cut = Render<TestStudentFormFields>(p => p
            .Add(x => x.Placement, StudentFormFields.StudentFormActionsPlacement.Right));

        var actionButtons = cut.FindAll(".form-actions .form-actions__button");
        actionButtons.Count.Should().Be(2,
            "the built-in Submit and Cancel buttons both carry the .form-actions__button class");
    }

    [TestMethod]
    public void Right_Placement_Wrapper_Has_Grid_Class_Chain()
    {
        // The CSS that gives the wrapper display:grid is keyed off the
        // .student-form-fields--sidebar > .student-form-fields__layout
        // selector (parent-descendant). Asserting the class chain is
        // present is sufficient — the actual computed style is browser-
        // only and the CSS rule itself is guarded by a source-level
        // test in StudentFormFieldsActionsPlacementTests.
        var cut = Render<TestStudentFormFields>(p => p
            .Add(x => x.Placement, StudentFormFields.StudentFormActionsPlacement.Right));

        var form = cut.Find("form.student-form-fields--sidebar");
        var wrapper = form.QuerySelector(".student-form-fields__layout");
        wrapper.Should().NotBeNull(
            "the layout wrapper sits inside the form which carries the --sidebar modifier class");
    }

    [TestMethod]
    public void Bottom_Placement_Wrapper_Is_Rendered_Inside_Form_Without_Sidebar_Class()
    {
        // Symmetric assertion: in Bottom placement the form does NOT
        // carry the --sidebar class, so the CSS that flips the wrapper
        // to display:grid does NOT apply, and the wrapper falls back
        // to display:contents (the default rule).
        var cut = Render<TestStudentFormFields>(p => p
            .Add(x => x.Placement, StudentFormFields.StudentFormActionsPlacement.Bottom));

        var form = cut.Find("form.student-form-fields:not(.student-form-fields--sidebar)");
        form.Should().NotBeNull("in bottom placement the form has only the base .student-form-fields class");
    }
}
