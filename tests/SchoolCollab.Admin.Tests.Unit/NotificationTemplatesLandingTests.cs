using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IndexPage = SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates.Index;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The landing grid (redesign spec §3.2), tested through the page seam for what the PAGE owns: each row
/// renders the kind banner, the contact mode, the linked title and the constraint count (a link at one
/// or more, the default badge at zero), and the row action opens the template''s address.
/// <para>
/// Filtering is proven at its own pure seam (<see cref="TemplateLandingFilterTests"/>): the toolbar is
/// published through a SectionContent the test renderer does not surface, and asserting the filter rule
/// directly is a better test than driving a dropdown that only forwards to it.
/// </para>
/// </summary>
[TestClass]
public class NotificationTemplatesLandingTests : BunitContext
{
    public NotificationTemplatesLandingTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private TemplateApiHandler Register() => TemplateTestServices.Register<IndexPage>(this);

    [TestMethod]
    public void Rows_RenderTheKindBannerTheModeTheTitleAndTheConstraintCount()
    {
        var handler = Register();
        handler.Rows.Add(new TemplateRow { Kind = 0, Channel = 0, Subject = "PUBLISH-DEFAULT", Body = "<p>default</p>" });
        handler.Rows.Add(new TemplateRow
        {
            Kind = 1,
            Channel = 1,
            Body = "REMINDER-TEXT",
            Constraints = [(0, TemplateApiHandler.GradeB)],
        });

        var cut = Render<IndexPage>();

        cut.WaitForAssertion(() =>
        {
            var banners = cut.FindAll(".kind-banner");
            banners.Should().HaveCount(2);
            banners.Select(b => b.GetAttribute("data-kind")).Should().BeEquivalentTo(["Publish", "Reminder"]);
            cut.Markup.Should().Contain("PUBLISH-DEFAULT");
            cut.Markup.Should().Contain("REMINDER-TEXT");
            cut.Markup.Should().Contain("Tenant default");
            cut.Markup.Should().Contain("1 constraint");
            cut.Markup.Should().Contain("SMS");
        });
    }

    [TestMethod]
    public void TheRowAction_OpensTheTemplateDetail()
    {
        var handler = Register();
        var row = new TemplateRow { Kind = 0, Channel = 0, Subject = "OPEN-ME" };
        handler.Rows.Add(row);

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("OPEN-ME"));

        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Open").Click();

        Services.GetRequiredService<NavigationManager>().Uri
            .Should().EndWith($"/notification-templates/{row.Id:D}");
    }
}