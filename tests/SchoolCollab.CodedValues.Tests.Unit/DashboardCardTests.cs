using Bunit;
using FluentAssertions;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Shared.Components.Dashboard;
using PeopleIcon = Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size24.People;

namespace SchoolCollab.CodedValues.Tests.Unit;

[TestClass]
public sealed class DashboardCardTests
{
    [TestMethod]
    public void DashboardCard_RendersStudentNavigationCard()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render(builder =>
        {
            builder.OpenComponent<DashboardCard>(0);
            builder.AddAttribute(1, nameof(DashboardCard.Href), "/students");
            builder.AddAttribute(2, nameof(DashboardCard.Title), "Students");
            builder.AddAttribute(3, nameof(DashboardCard.Description), "Manage student records.");
            builder.AddAttribute(4, nameof(DashboardCard.Icon), new PeopleIcon());
            builder.CloseComponent();
        });

        var card = cut.FindComponents<DashboardCard>().Single();

        card.Markup.Should().Contain("Students");
        cut.Markup.Should().Contain("Manage student records.");
        cut.Markup.Should().Contain("Href=\"/students\"");
    }

    [TestMethod]
    public void DashboardCard_ClipsLongDescriptionAndKeepsFullTextInTitleAttribute()
    {
        using var ctx = new BunitContext();

        var longDescription =
            "Manage student records, enrollments, grade levels, subjects, periods, guardians, " +
            "and historical transcripts across multiple academic years for the entire district.";

        var cut = ctx.Render(builder =>
        {
            builder.OpenComponent<DashboardCard>(0);
            builder.AddAttribute(1, nameof(DashboardCard.Href), "/students");
            builder.AddAttribute(2, nameof(DashboardCard.Title), "Students");
            builder.AddAttribute(3, nameof(DashboardCard.Description), longDescription);
            builder.AddAttribute(4, nameof(DashboardCard.Icon), new PeopleIcon());
            builder.AddAttribute(5, nameof(DashboardCard.MaxDescriptionLength), 60);
            builder.CloseComponent();
        });

        var paragraph = cut.Find(".home-card-description");

        // Visible text is clipped and ends with the ellipsis character.
        paragraph.TextContent.Should().EndWith("…");
        paragraph.TextContent.Length.Should().BeLessThan(longDescription.Length);

        // The browser tooltip always carries the uncut description.
        paragraph.GetAttribute("title").Should().Be(longDescription);
    }

    [TestMethod]
    public void DashboardCard_DoesNotClipShortDescription()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render(builder =>
        {
            builder.OpenComponent<DashboardCard>(0);
            builder.AddAttribute(1, nameof(DashboardCard.Href), "/students");
            builder.AddAttribute(2, nameof(DashboardCard.Title), "Students");
            builder.AddAttribute(3, nameof(DashboardCard.Description), "Manage student records.");
            builder.AddAttribute(4, nameof(DashboardCard.Icon), new PeopleIcon());
            builder.CloseComponent();
        });

        var paragraph = cut.Find(".home-card-description");

        paragraph.TextContent.Should().Be("Manage student records.");
        paragraph.TextContent.Should().NotEndWith("…");
        paragraph.GetAttribute("title").Should().Be("Manage student records.");
    }

    [TestMethod]
    public void DashboardCard_DescriptionHasLineClampClass()
    {
        // Regression guard: the description <p> must carry the
        // `.home-card-description` class so the line-clamp CSS engages.
        // Without it, the Students card on Home.razor was reported as
        // taller than its siblings because its longer description wrapped
        // to an extra line and the FluentCard's height: fit-content
        // expanded to fit the content.
        using var ctx = new BunitContext();

        var cut = ctx.Render(builder =>
        {
            builder.OpenComponent<DashboardCard>(0);
            builder.AddAttribute(1, nameof(DashboardCard.Href), "/students");
            builder.AddAttribute(2, nameof(DashboardCard.Title), "Students");
            builder.AddAttribute(3, nameof(DashboardCard.Description), "Manage student records, enrollments, grade levels, subjects, and periods.");
            builder.AddAttribute(4, nameof(DashboardCard.Icon), new PeopleIcon());
            builder.CloseComponent();
        });

        var paragraph = cut.Find(".home-card-description");

        paragraph.ClassList.Should().Contain("home-card-description");
    }

    [TestMethod]
    public void DashboardCard_TitleAttributeCarriesFullDescriptionRegardlessOfClass()
    {
        // Even with the line-clamp class applied, hover must still expose
        // the full uncut description to the user.
        using var ctx = new BunitContext();

        var fullDescription = "Manage student records, enrollments, grade levels, subjects, and periods.";

        var cut = ctx.Render(builder =>
        {
            builder.OpenComponent<DashboardCard>(0);
            builder.AddAttribute(1, nameof(DashboardCard.Href), "/students");
            builder.AddAttribute(2, nameof(DashboardCard.Title), "Students");
            builder.AddAttribute(3, nameof(DashboardCard.Description), fullDescription);
            builder.AddAttribute(4, nameof(DashboardCard.Icon), new PeopleIcon());
            builder.CloseComponent();
        });

        var paragraph = cut.Find(".home-card-description");

        paragraph.GetAttribute("title").Should().Be(fullDescription);
    }

    [TestMethod]
    public void DashboardCard_CssContainsLineClampRule()
    {
        // Regression guard at the CSS level: the line-clamp rule must
        // exist so the clamp class actually constrains the description.
        // If someone removes the class on the <p> or drops the CSS rule,
        // the Home page Students card re-grows taller than its siblings.
        //
        // The description clamp rule is owned by DashboardCard itself — it
        // lives in DashboardCard.razor.css (CSS-isolated). The shell/layout
        // rules live in DashboardSection.razor.css; both files replaced the
        // original global wwwroot/css/app.css dashboard block and both now
        // live under the grouped Components/Dashboard/ solution folder.
        var cssPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "SchoolCollab.Admin.Shared", "Components", "Dashboard", "DashboardCard.razor.css");
        cssPath = Path.GetFullPath(cssPath);

        File.Exists(cssPath).Should().BeTrue($"expected DashboardCard.razor.css to exist at {cssPath}");

        var css = File.ReadAllText(cssPath);

        css.Should().Contain(".home-card-description", "the <p> element relies on this class for its line-clamp rule");
        css.Should().Contain("-webkit-line-clamp: 3", "the description must be clamped to a fixed number of lines so cards in a row share height");
        css.Should().Contain("-webkit-box-orient: vertical", "line-clamp requires -webkit-box with vertical orientation");
        css.Should().Contain("overflow: hidden", "clamped content must be hidden so it does not push the card taller");
        css.Should().Contain("line-height: 1.4", "a fixed line-height makes the reserved description height deterministic");
        css.Should().Contain("min-height: calc(1.4em * 3)", "reserving 3 line-boxes equalises description space across cards so a shorter description cannot leave its card shorter than the tallest sibling");
    }

    [TestMethod]
    public void DashboardCard_HomeRendersAllThreeCardsWithEqualDescriptionStructure()
    {
        // The exact scenario reported on SchoolCollab.Admin Home.razor:
        // three cards (Coded Values, Assignments, Students) with three
        // different description lengths, all rendered with the same
        // line-clamped paragraph so the cards share row height.
        using var ctx = new BunitContext();

        var cut = ctx.Render(builder =>
        {
            builder.OpenComponent<DashboardCard>(0);
            builder.AddAttribute(1, nameof(DashboardCard.Href), "/coded-values");
            builder.AddAttribute(2, nameof(DashboardCard.Title), "Coded Values");
            builder.AddAttribute(3, nameof(DashboardCard.Description), "Manage categories, subjects, grades, and other reference data.");
            builder.AddAttribute(4, nameof(DashboardCard.Icon), new Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size24.Tag());
            builder.CloseComponent();

            builder.OpenComponent<DashboardCard>(1);
            builder.AddAttribute(2, nameof(DashboardCard.Href), "/assignments");
            builder.AddAttribute(3, nameof(DashboardCard.Title), "Assignments");
            builder.AddAttribute(4, nameof(DashboardCard.Description), "Create, publish, and review assignments and homework.");
            builder.AddAttribute(5, nameof(DashboardCard.Icon), new Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size24.ClipboardCheckmark());
            builder.CloseComponent();

            builder.OpenComponent<DashboardCard>(2);
            builder.AddAttribute(3, nameof(DashboardCard.Href), "/students");
            builder.AddAttribute(4, nameof(DashboardCard.Title), "Students");
            builder.AddAttribute(5, nameof(DashboardCard.Description), "Manage student records, enrollments, grade levels, subjects, and periods.");
            builder.AddAttribute(6, nameof(DashboardCard.Icon), new PeopleIcon());
            builder.CloseComponent();
        });

        var paragraphs = cut.FindAll(".home-card-description");

        paragraphs.Should().HaveCount(3);

        // Every description <p> must carry the clamp class and the
        // uncut title text. This is the structural guarantee that
        // prevents a single long description from pushing one card
        // taller than the rest.
        var titles = paragraphs.Select(p => p.GetAttribute("title")).ToList();
        titles.Should().AllSatisfy(t => t.Should().NotBeNullOrWhiteSpace());

        paragraphs.Should().AllSatisfy(p =>
        {
            p.ClassList.Should().Contain("home-card-description");
        });

        // Sanity check: each <p> is wrapped in a `.home-card-inner` so
        // the clamp rule (selector .home-card-description) is the only
        // path that constrains the description; nothing else in the
        // ancestor chain accidentally resets it.
        var innerWrappers = cut.FindAll(".home-card-inner");
        innerWrappers.Should().HaveCount(3);
        innerWrappers.Should().AllSatisfy(w => w.GetElementsByClassName("home-card-description").Should().HaveCount(1));
    }
}