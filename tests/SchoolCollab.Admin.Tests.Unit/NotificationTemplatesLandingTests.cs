using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IndexPage = SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates.Index;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The landing grid (redesign spec 3.2), tested through the page seam for what the PAGE owns: each row
/// renders the kind banner, the owner, the contact mode, the linked title and the constraint count (a
/// link at one or more, the scope's default badge at zero), and the kebab offers exactly the actions
/// this session may take on that row.
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

    private TemplateApiHandler Register(bool realTenant = true) =>
        TemplateTestServices.Register<IndexPage>(this, realTenant);

    private static string[] MenuItems(IRenderedComponent<IndexPage> cut) =>
        cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToArray();

    [TestMethod]
    public void Rows_RenderTheKindBannerTheModeTheOwnerAndTheConstraintCount()
    {
        var handler = Register();
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 0, Subject = "SYSTEM-DEFAULT", Body = "<p>default</p>",
            TenantId = null, OwnerName = "System",
        });
        handler.Rows.Add(new TemplateRow
        {
            Kind = 1,
            Channel = 1,
            Body = "REMINDER-TEXT",
            Constraints = [(0, TemplateApiHandler.GradeB)],
            TenantId = TemplateTestServices.RealTenant,
            OwnerName = "Northfield",
        });

        var cut = Render<IndexPage>();

        cut.WaitForAssertion(() =>
        {
            var banners = cut.FindAll(".kind-banner");
            banners.Should().HaveCount(2);
            banners.Select(b => b.GetAttribute("data-kind")).Should().BeEquivalentTo(["Publish", "Reminder"]);
            cut.Markup.Should().Contain("SYSTEM-DEFAULT");
            cut.Markup.Should().Contain("REMINDER-TEXT");
            cut.FindAll("fluent-badge").Select(b => b.TextContent.Trim())
                .Should().Contain("System", "the blueprint row's OWNER badge is labelled as such (spec 3.2)");
            cut.Find(".template-owner").TextContent.Trim().Should().Be("Northfield",
                "and a tenant row carries its owner's name");
            cut.Markup.Should().Contain("System default", "the zero-constraint badge names the SCOPE");
            cut.Markup.Should().Contain("1 constraint");
            cut.Markup.Should().Contain("SMS");
        });
    }

    [TestMethod]
    public void TheRowAction_OpensTheTemplateDetail()
    {
        var handler = Register();
        var row = new TemplateRow
        {
            Kind = 0, Channel = 0, Subject = "OPEN-ME",
            TenantId = TemplateTestServices.RealTenant, OwnerName = "Northfield",
        };
        handler.Rows.Add(row);

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("OPEN-ME"));

        cut.FindAll("fluent-menu-item").Single(i => i.TextContent.Trim() == "Open").MenuItemClick();

        Services.GetRequiredService<NavigationManager>().Uri
            .Should().EndWith($"/notification-templates/{row.Id:D}");
    }

    [TestMethod]
    public void InATenantSession_ASystemRowOffersAdopt_AndATenantRowOffersResetInstead()
    {
        var handler = Register(realTenant: true);
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 0, Subject = "BLUEPRINT", TenantId = null, OwnerName = "System",
        });
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 1, Subject = "MINE",
            TenantId = TemplateTestServices.RealTenant, OwnerName = "Northfield",
        });

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("BLUEPRINT"));

        var items = MenuItems(cut);
        items.Should().Contain("Adopt to this tenant", "a tenant may copy the blueprint (spec 3.8)");
        items.Should().NotContain("Publish portal-wide...", "promotion belongs to the system scope");
        items.Should().Contain("Reset to built-in default...", "the tenant's own row is writable");
    }

    [TestMethod]
    public void InSystemScope_ATenantRowOffersPublishPortalWide_AndReadsEveryTenantsRows()
    {
        var handler = Register(realTenant: false);
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 0, Subject = "NORTHFIELD-ROW",
            TenantId = TemplateTestServices.RealTenant, OwnerName = "Northfield",
        });

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("NORTHFIELD-ROW"));

        MenuItems(cut).Should().Contain("Publish portal-wide...",
            "the system session promotes a tenant row into the blueprint (spec 3.8)");

        handler.Calls.Should().Contain(c => c.Url.Contains("includeTenantOwned=true", StringComparison.Ordinal),
            "the promote picker's source is the cross-tenant read (spec 3.6)");
    }

    [TestMethod]
    public void AGradeDeepLink_NamesTheFilter_AndTheBarWidensTheViewAgain()
    {
        var handler = Register();
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 0, Subject = "GRADE-A-ROW",
            Constraints = [(0, TemplateApiHandler.GradeA)],
        });
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 1, Subject = "GRADE-B-ROW",
            Constraints = [(0, TemplateApiHandler.GradeB)],
        });

        // A [SupplyParameterFromQuery] value cannot be pushed through the parameter builder (bUnit
        // refuses it); the deep link is modelled the way the browser arrives - navigate, then render.
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("grade", TemplateApiHandler.GradeA));

        var cut = Render<IndexPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("#template-grade-filter-bar").TextContent.Should().Contain("Filtered to one grade level",
                "a filtered landing must name its filter (spec 3.2)");
            cut.Markup.Should().Contain("GRADE-A-ROW");
            cut.Markup.Should().NotContain("GRADE-B-ROW", "the deep link filters the rows");
        });

        cut.Find("#template-grade-filter-clear").Click();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("GRADE-B-ROW", "the bar's button widens the view again"));
    }
}