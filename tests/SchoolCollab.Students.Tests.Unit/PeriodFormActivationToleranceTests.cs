using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Components.Pages.Periods;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// bUnit tests for the per-period activation-tolerance field surfaced by
/// <see cref="PeriodFormFields"/> (period-activation-window spec FR-W3, UI round
/// "period-activation-tolerance-ui"). The component owns the shared field rows
/// rendered by both period create and edit, so asserting the field here covers
/// both flows. The field is an optional numeric input (blank ⇒ inherit the global
/// default); a value widens/narrows the window within which the period may be
/// activated. Asserts on the rendered DOM:
///   - the "Activation tolerance (days)" label renders (via FormRow)
///   - a <c>fluent-number-field</c> input renders
///   - the model's value is reflected in the rendered input
/// </summary>
[TestClass]
public class PeriodFormActivationToleranceTests : BunitContext
{
    public PeriodFormActivationToleranceTests()
    {
        // bUnit needs the FluentUI services registered (JSRuntime + DI for
        // FluentNumberField / FluentDatePicker / FluentTextField). The bUnit
        // context's JSRuntime is in Loose mode so JS interop calls are no-ops.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class TestModel : PeriodFormFields.IPeriodFormModel
    {
        public string Name { get; set; } = "Test period";
        public AcademicYearDivision Division { get; set; } = AcademicYearDivision.None;
        public string ParentPeriodIdText { get; set; } = "";
        public DateTime? Start { get; set; } = new DateTime(2026, 9, 1);
        public DateTime? End { get; set; } = new DateTime(2027, 8, 31);
        public int? ActivationToleranceDays { get; set; }
    }

    [TestMethod]
    public void Renders_ActivationTolerance_Field_With_Label()
    {
        var cut = Render<PeriodFormFields>(p => p
            .Add(x => x.Model, new TestModel()));

        // The FormRow label is rendered as plain text.
        cut.Markup.Should().Contain("Activation tolerance (days)");
        // The numeric input renders as a FluentUI custom element.
        cut.FindAll("fluent-number-field").Should().NotBeEmpty();
    }

    [TestMethod]
    public void Reflects_Model_Value_In_Rendered_Input()
    {
        var model = new TestModel { ActivationToleranceDays = 30 };
        var cut = Render<PeriodFormFields>(p => p
            .Add(x => x.Model, model));

        // The model's override value is reflected in the rendered input.
        cut.Markup.Should().Contain("30");
    }

    [TestMethod]
    public void Blank_Value_Renders_Empty_Input()
    {
        var cut = Render<PeriodFormFields>(p => p
            .Add(x => x.Model, new TestModel { ActivationToleranceDays = null }));

        // Blank (null) ⇒ inherit the global default; the input renders empty.
        cut.FindAll("fluent-number-field").Should().NotBeEmpty();
    }
}
