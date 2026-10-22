using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IndexPage = SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates.Index;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The landing grid (redesign spec 3.2; naming spec D3/D4/D7/D11), tested through the page seam for what
/// the PAGE owns: each row renders the kind banner, the owner, the contact mode, the generated CODE (a
/// dash for a pre-upgrade row), the TITLE as the link with the description beneath it, and the constraint
/// count (a link at one or more, the scope's default badge at zero); the kebab offers exactly the actions
/// this session may take on that row; and every prompt it opens — adopt, publish portal-wide, reset —
/// names the row by its title.
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

    private TemplateDialogs RegisterDialogs() => TemplateDialogs.Register(this);

    private static string[] MenuItems(IRenderedComponent<IndexPage> cut) =>
        cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToArray();

    private static IElement MenuItem(IRenderedComponent<IndexPage> cut, string label) =>
        cut.FindAll("fluent-menu-item").Single(i => i.TextContent.Trim() == label);

    [TestMethod]
    public void Rows_RenderTheKindBannerTheModeTheOwnerAndTheConstraintCount()
    {
        var handler = Register();
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 0, Title = "SYSTEM-DEFAULT", Body = "<p>default</p>",
            TenantId = null, OwnerName = "System",
        });
        handler.Rows.Add(new TemplateRow
        {
            Kind = 1,
            Channel = 1,
            Title = "REMINDER-TEXT",
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
    public void TheCodeColumn_RendersTheGeneratedCode_AndADashForAPreUpgradeRow()
    {
        // Arrange — D7: a row that predates the code column was never backfilled.
        var handler = Register();
        handler.Rows.Add(new TemplateRow { Kind = 0, Channel = 0, Title = "Minted on create", Code = "TPL07" });
        handler.Rows.Add(new TemplateRow { Kind = 1, Channel = 1, Title = "From before the upgrade", Code = null });

        // Act
        var cut = Render<IndexPage>();

        // Assert — the grid names such a row with a dash rather than an empty cell the operator would
        // read as a bug.
        cut.WaitForAssertion(() =>
            cut.FindAll(".template-code").Select(c => c.TextContent.Trim())
                .Should().Equal(["TPL07", "—"],
                    "the minted code is shown, and a pre-upgrade row shows a dash (D7)"));
    }

    [TestMethod]
    public void TheTemplateCell_IsTheTitleLink_WithTheDescriptionBeneathIt()
    {
        // Arrange
        var handler = Register();
        var row = new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Title = "Grade 8 Science reminder",
            Description = "Sent the day before the lab.",
            Subject = "A subject that is content, not a name",
        };
        handler.Rows.Add(row);

        // Act
        var cut = Render<IndexPage>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var cell = cut.Find(".template-cell");
            var link = cell.QuerySelector("fluent-link")!;
            link.TextContent.Trim().Should().Be("Grade 8 Science reminder",
                "the template cell is the row's identity: its title (D3)");
            link.GetAttribute("href").Should().Be($"/notification-templates/{row.Id:D}",
                "and it still opens the row");
            cell.QuerySelector(".template-cell__description")!.TextContent.Trim()
                .Should().Be("Sent the day before the lab.", "the description scans as a muted second line (D4)");
            cut.Markup.Should().NotContain("A subject that is content, not a name",
                "the subject line is email content and never identifies a row (D3)");
        });
    }

    [TestMethod]
    public void AReset_AsksForAReasonInTheSharedDialog_NamingTheTitle_ThenDeletesWithIt()
    {
        // Arrange
        var handler = Register();
        var dialogs = RegisterDialogs();
        handler.Rows.Add(new TemplateRow { Kind = 0, Channel = 0, Title = "Grade 8 Science reminder" });

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 8 Science reminder"));

        // Act
        dialogs.ReasonAnswer = "Superseded by the new reminder";
        MenuItem(cut, "Reset to built-in default...").MenuItemClick();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var delete = handler.Calls.Single(c => c.Method == "DELETE");
            delete.Url.Should().Contain("reason=Superseded%20by%20the%20new%20reminder",
                "the reset is audited with the operator's own words (D11)");
            cut.Markup.Should().NotContain("Grade 8 Science reminder",
                "the row leaves the grid optimistically, without re-reading the list");
        });

        dialogs.ReasonOpenCount.Should().Be(1, "the reason dialog IS the reset's confirmation (D11)");
        dialogs.LastReasonTitle.Should().Contain("Grade 8 Science reminder",
            "the prompt names the row it is about to remove (D3)");
    }

    [TestMethod]
    public void AReset_DismissedInTheReasonDialog_WritesNothing_AndKeepsTheRow()
    {
        // Arrange
        var handler = Register();
        var dialogs = RegisterDialogs();
        handler.Rows.Add(new TemplateRow { Kind = 0, Channel = 0, Title = "Grade 8 Science reminder" });

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 8 Science reminder"));

        // Act
        dialogs.ReasonAnswer = null;
        MenuItem(cut, "Reset to built-in default...").MenuItemClick();

        // Assert
        cut.WaitForAssertion(() => dialogs.ReasonOpenCount.Should().Be(1));
        handler.Calls.Should().NotContain(c => c.Method == "DELETE", "no reason, no reset");
        cut.Markup.Should().Contain("Grade 8 Science reminder", "the row is still there");
    }

    [TestMethod]
    public void TheRowAction_OpensTheTemplateDetail()
    {
        var handler = Register();
        var row = new TemplateRow
        {
            Kind = 0, Channel = 0, Title = "OPEN-ME",
            TenantId = TemplateTestServices.RealTenant, OwnerName = "Northfield",
        };
        handler.Rows.Add(row);

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("OPEN-ME"));

        MenuItem(cut, "Open").MenuItemClick();

        Services.GetRequiredService<NavigationManager>().Uri
            .Should().EndWith($"/notification-templates/{row.Id:D}");
    }

    [TestMethod]
    public void InATenantSession_ASystemRowOffersAdopt_AndATenantRowOffersResetInstead()
    {
        var handler = Register(realTenant: true);
        RegisterDialogs();
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 0, Title = "BLUEPRINT", TenantId = null, OwnerName = "System",
        });
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 1, Title = "MINE",
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
    public void Adopt_ConfirmsTheCopy_NamingTheRowByItsTitle()
    {
        // Arrange
        var handler = Register(realTenant: true);
        var dialogs = RegisterDialogs();
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 0, Title = "Grade 8 Science reminder", TenantId = null, OwnerName = "System",
        });

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 8 Science reminder"));

        // Act — the operator reads the prompt and backs out.
        dialogs.ConfirmAnswer = false;
        MenuItem(cut, "Adopt to this tenant").MenuItemClick();

        // Assert
        cut.WaitForAssertion(() =>
        {
            dialogs.LastConfirmMessage.Should().Contain("Grade 8 Science reminder",
                "the confirmation names the row the operator is about to copy (D3)");
        });
        handler.Calls.Should().NotContain(c => c.Url.Contains("/adopt", StringComparison.Ordinal),
            "a refused confirmation copies nothing");
    }

    [TestMethod]
    public void InSystemScope_ATenantRowOffersPublishPortalWide_AndReadsEveryTenantsRows()
    {
        var handler = Register(realTenant: false);
        RegisterDialogs();
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 0, Title = "NORTHFIELD-ROW",
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
    public void PublishPortalWide_ConfirmsTheCopy_NamingTheRowByItsTitle()
    {
        // Arrange
        var handler = Register(realTenant: false);
        var dialogs = RegisterDialogs();
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 0, Title = "Grade 8 Science reminder",
            TenantId = TemplateTestServices.RealTenant, OwnerName = "Northfield",
        });

        var cut = Render<IndexPage>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 8 Science reminder"));

        // Act
        dialogs.ConfirmAnswer = false;
        MenuItem(cut, "Publish portal-wide...").MenuItemClick();

        // Assert
        cut.WaitForAssertion(() => dialogs.LastConfirmMessage.Should().Contain("Grade 8 Science reminder",
            "the confirmation names the row the operator is about to publish (D3)"));
        handler.Calls.Should().NotContain(c => c.Url.Contains("/promote", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AGradeDeepLink_NamesTheFilter_AndTheBarWidensTheViewAgain()
    {
        var handler = Register();
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 0, Title = "GRADE-A-ROW",
            Constraints = [(0, TemplateApiHandler.GradeA)],
        });
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0, Channel = 1, Title = "GRADE-B-ROW",
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
