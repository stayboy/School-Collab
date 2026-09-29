using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Components.Pages.Periods;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// The Sequence row's OPTIONS (subject-period-exception-model.md v5 §0 decision 15, slice 4b):
/// the period form must offer only positions a sibling sub-period of the same academic year and
/// division does NOT already hold, always keeping the position the row itself holds so an edit can
/// save without moving itself, and reaching any higher position the tenant has declared.
///
/// <para>This is the CONTROL half of the rule. The authority is the API's own pre-check
/// (<c>PeriodSequenceGuard</c>, a 422 naming the sibling — see
/// <see cref="PeriodSequenceTakenTests"/>) and behind that the filtered unique index; the dropdown
/// exists so the invalid choice is not offered in the first place. The component is rendered
/// directly, as <see cref="PeriodFormActivationToleranceTests"/> does, because it is the shared
/// field set behind BOTH period flows.</para>
/// </summary>
[TestClass]
public class PeriodFormSequenceOptionsTests : BunitContext
{
    private static readonly Guid YearId = Guid.Parse("0e000000-0000-0000-0000-0000000000ff");

    public PeriodFormSequenceOptionsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class TestModel : PeriodFormFields.IPeriodFormModel
    {
        public string Name { get; set; } = "Term 1";
        public AcademicYearDivision Division { get; set; } = AcademicYearDivision.Terms;
        public string ParentPeriodIdText { get; set; } = YearId.ToString();
        public DateTime? Start { get; set; } = new DateTime(2027, 1, 1);
        public DateTime? End { get; set; } = new DateTime(2027, 6, 30);
        public int? ActivationToleranceDays { get; set; }
        public int? Sequence { get; set; }
    }

    /// <summary>The positions the Sequence row offers, read off the bound select's items — a
    /// FluentSelect does not materialise its options while closed, so this is the only honest way
    /// to read an offer set.</summary>
    private static int[] Offered(IRenderedComponent<PeriodFormFields> cut) =>
        [.. cut.FindComponent<FluentSelect<int>>().Instance.Items!];

    private IRenderedComponent<PeriodFormFields> RenderSequenceRow(
        int? sequence, params int[] unavailable) =>
        Render<PeriodFormFields>(p => p
            .Add(x => x.Model, new TestModel { Sequence = sequence })
            .Add(x => x.UnavailableSequences, unavailable));

    [TestMethod]
    public void Offers_OneToFour_BeforeAnySiblingIsConsidered()
    {
        var offered = Offered(RenderSequenceRow(sequence: null));

        offered.Should().BeEquivalentTo(new[] { 1, 2, 3, 4 },
            "1st–4th are always available: a position that can be NAMED is what makes Q1 possible");
    }

    [TestMethod]
    public void Omits_EveryPositionASiblingHolds()
    {
        var offered = Offered(RenderSequenceRow(sequence: null, unavailable: [1, 3]));

        offered.Should().BeEquivalentTo(new[] { 2, 4 },
            "offering a position the server would reject is exactly what the control exists to avoid");
    }

    [TestMethod]
    public void Keeps_TheRowsOwnPosition_OnOffer()
    {
        // The page EXCLUDES the row being edited from UnavailableSequences, so a period that
        // already holds a position is not a collision with itself and can be saved unchanged.
        var offered = Offered(RenderSequenceRow(sequence: 3, unavailable: [1]));

        offered.Should().Contain(3, "an edit form must be able to keep the position it already holds");
    }

    [TestMethod]
    public void Reaches_AnyHigherDeclaredPosition()
    {
        // Q5's ladder: a backfilled "Term 5" makes 5th representable — without this the bound
        // value would be absent from Items and silently dropped on save.
        var offered = Offered(RenderSequenceRow(sequence: 5, unavailable: [2]));

        offered.Should().BeEquivalentTo(new[] { 1, 3, 4, 5 },
            "the ladder extends to the highest position this row or a sibling holds");
    }

    [TestMethod]
    public void Renders_NoSequenceRow_ForATopLevelAcademicYear()
    {
        var cut = Render<PeriodFormFields>(p => p
            .Add(x => x.Model, new TestModel { ParentPeriodIdText = "", Sequence = null }));

        cut.Markup.Should().Contain("Period name", "the form itself rendered");
        cut.FindComponents<FluentSelect<int>>().Should().BeEmpty(
            "a top-level academic year is not the 1st of anything — the entity and the filtered index both refuse a position on it");
    }
}
