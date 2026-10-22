using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The shared editor's surface (naming spec D1/D9/D12), asserted through the rendered page rather than
/// through its stylesheet: the naming rows sit above one "Template Type" row that holds BOTH the kind
/// and the contact-mode dropdown, the action bar repeats neither the kind nor the page's address, and
/// "View all constraints" waits until the chip row stops being scannable.
/// </summary>
[TestClass]
public class TemplateEditorSurfaceTests : BunitContext
{
    public TemplateEditorSurfaceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private TemplateApiHandler Register() => TemplateTestServices.Register<TemplateEditor>(this);

    private IRenderedComponent<TemplateEditor> RenderCreate() =>
        Render<TemplateEditor>(p => p.Add(x => x.Mode, TemplateEditorMode.Create));

    private IRenderedComponent<TemplateEditor> RenderEdit(Guid id) =>
        Render<TemplateEditor>(p => p
            .Add(x => x.Mode, TemplateEditorMode.Edit)
            .Add(x => x.Id, id));

    /// <summary>The FormRow whose label is <paramref name="label"/> — the row is the unit under test.</summary>
    private static IRenderedComponent<FormRow> RowLabelled(
        IRenderedComponent<TemplateEditor> cut, string label) =>
        cut.FindComponents<FormRow>().Single(r => r.Instance.Label == label);

    /// <summary>The <c>.form-row</c> a control sits in.</summary>
    private static IElement RowOf(IElement control) =>
        control.Ancestors().OfType<IElement>().First(a => a.ClassList.Contains("form-row"));

    [TestMethod]
    public void TheTemplateTypeRow_HoldsBothDropdowns_InOneRow()
    {
        Register();
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-title").Should().ContainSingle());

        var dropdowns = cut.FindAll("[aria-label='Template type'], [aria-label='Preferred mode of contact']");
        dropdowns.Should().HaveCount(2, "the kind and the contact mode are both rendered");

        var rows = dropdowns.Select(RowOf).Distinct().ToList();
        rows.Should().ContainSingle(
            "type and preferred mode of contact are ONE decision, so they share one row (D9)");
        rows[0].QuerySelector(".form-row-label")!.TextContent.TrimStart()
            .Should().StartWith("Template Type", "…under the one label that names the decision");
    }

    [TestMethod]
    public void TheNamingRows_SitAboveTheTemplateTypeRow_WithARequiredTitle()
    {
        Register();
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-title").Should().ContainSingle());

        var labels = cut.FindComponents<FormRow>().Select(r => r.Instance.Label).ToList();

        labels.IndexOf("Title").Should().BeGreaterThanOrEqualTo(0, "a template is named on create (D1)");
        labels.IndexOf("Title").Should().BeLessThan(labels.IndexOf("Template Type"));
        labels.IndexOf("Description").Should().BeGreaterThan(labels.IndexOf("Title"));
        labels.IndexOf("Description").Should().BeLessThan(labels.IndexOf("Template Type"));

        RowLabelled(cut, "Title").Instance.Required.Should().BeTrue(
            "no template may be created nameless (D1/D2)");
        RowLabelled(cut, "Description").Instance.Required.Should().BeFalse(
            "the description is optional metadata (D1)");
    }

    [TestMethod]
    public void TheActionBar_RepeatsNeitherTheKindNorThePagesAddress()
    {
        Register();
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-save").Should().ContainSingle());

        var actionBar = cut.Find(".authoring-actionbar");

        actionBar.QuerySelectorAll(".kind-banner").Should().BeEmpty(
            "the kind is chosen a few centimetres below the bar, so a banner there says it twice (D9)");
        actionBar.TextContent.Should().NotContain("New template",
            "the breadcrumb is the one place that says where the operator is (D9)");
        cut.Find("#authoring-breadcrumb").TextContent.Should().Contain("New template",
            "…and it still says it");
    }

    [TestMethod]
    public void OnEdit_TheActionBarHeading_IsTheTemplatesTitle_AndTheOwnerStaysBesideIt()
    {
        var handler = Register();
        var row = new TemplateRow
        {
            Kind = 0, Channel = 1, Title = "Grade 8 Science reminder", Body = "BODY",
        };
        handler.Rows.Add(row);

        var cut = RenderEdit(row.Id);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".authoring-heading").TextContent.Should().Contain("Grade 8 Science reminder",
                "the page names itself by the row it has open (D3/D9)");
            cut.FindAll(".authoring-actionbar .kind-banner").Should().BeEmpty("still no kind banner");
            cut.Find("#template-editor-owner").Should().NotBeNull(
                "the owner chip stays on edit, where it says whose row this is (D9)");
        });
    }

    [TestMethod]
    public void ViewAllConstraints_AppearsOnlyOnceTheChipRowStopsBeingScannable()
    {
        var handler = Register();
        var atTheThreshold = new TemplateRow
        {
            Kind = 0,
            Channel = 1,
            Title = "Five constraints",
            Body = "BODY",
            Constraints = [.. Enumerable.Range(1, 5).Select(i => (1, $"stream-{i}"))],
        };
        var aboveTheThreshold = new TemplateRow
        {
            Kind = 0,
            Channel = 1,
            Title = "Six constraints",
            Body = "BODY",
            Constraints = [.. Enumerable.Range(1, 6).Select(i => (1, $"stream-{i}"))],
        };
        handler.Rows.AddRange([atTheThreshold, aboveTheThreshold]);

        var five = RenderEdit(atTheThreshold.Id);
        five.WaitForAssertion(() => five.FindAll("#template-constraints-chips").Should().ContainSingle());
        five.FindAll("#template-constraints-view-all").Should().BeEmpty(
            "five constraints still fit the chip row, so the link would only duplicate it (D12)");

        var six = RenderEdit(aboveTheThreshold.Id);
        six.WaitForAssertion(() => six.FindAll("#template-constraints-chips").Should().ContainSingle());
        six.Find("#template-constraints-view-all").TextContent.Should().Contain("View all constraints",
            "past five constraints the full list earns its one link (D12)");
    }
}
