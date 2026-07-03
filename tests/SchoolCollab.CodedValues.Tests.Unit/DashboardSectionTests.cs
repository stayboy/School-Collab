using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Shared.Components.Dashboard;

namespace SchoolCollab.CodedValues.Tests.Unit;

[TestClass]
public sealed class DashboardSectionTests
{
    // A couple of concrete icon instances used across the tests. The list
    // deliberately mixes different concrete types to prove the Items path
    // handles heterogeneous icons (which the old generic DashboardCard<TIcon>
    // could not express in a single list).
    private static readonly Icon TagIcon = new Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size24.Tag();
    private static readonly Icon PeopleIcon = new Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size24.People();

    [TestMethod]
    public void Items_RendersOneCardPerItemWithCorrectAttributes()
    {
        using var ctx = new BunitContext();

        var items = new List<DashboardItem>
        {
            new(Href: "/coded-values", Title: "Coded Values",
                Description: "Manage reference data.", Icon: TagIcon),
            new(Href: "/students", Title: "Students",
                Description: "Manage student records.", Icon: PeopleIcon),
        };

        var cut = ctx.Render(builder =>
        {
            builder.OpenComponent<DashboardSection>(0);
            builder.AddAttribute(1, nameof(DashboardSection.Title), "Home");
            builder.AddAttribute(2, nameof(DashboardSection.Items), items);
            builder.CloseComponent();
        });

        // One card-link per item, each carrying its Href.
        var links = cut.FindAll("a.card-link");
        links.Should().HaveCount(2);
        links[0].GetAttribute("href").Should().Be("/coded-values");
        links[1].GetAttribute("href").Should().Be("/students");

        // Titles and descriptions propagate to the rendered card markup.
        cut.Markup.Should().Contain("Coded Values");
        cut.Markup.Should().Contain("Manage reference data.");
        cut.Markup.Should().Contain("Students");
        cut.Markup.Should().Contain("Manage student records.");

        // Each card renders a DashboardCard child instance.
        cut.FindComponents<DashboardCard>().Should().HaveCount(2);
    }

    [TestMethod]
    public void ChildContent_RendersWhenItemsIsNull()
    {
        // The ChildContent escape hatch must still work so a page can author
        // bespoke markup (Settings.razor keeps this path).
        using var ctx = new BunitContext();

        var cut = ctx.Render(builder =>
        {
            builder.OpenComponent<DashboardSection>(0);
            builder.AddAttribute(1, nameof(DashboardSection.Title), "Section");
            builder.AddAttribute(2, nameof(DashboardSection.ChildContent),
                (RenderFragment)(b =>
                {
                    b.AddMarkupContent(0, "<p class=\"bespoke\">hand-authored</p>");
                }));
            builder.CloseComponent();
        });

        cut.Find(".bespoke").TextContent.Should().Be("hand-authored");
        // No DashboardCard children when Items is null and ChildContent is bespoke.
        cut.FindAll("a.card-link").Should().BeEmpty();
    }

    [TestMethod]
    public void Items_WinsOverChildContentWhenBothSet()
    {
        // The contract documented on DashboardSection.Items: when set it
        // renders cards and ignores ChildContent. This guards against a page
        // accidentally supplying both and seeing stale hand-authored markup.
        using var ctx = new BunitContext();

        var items = new List<DashboardItem>
        {
            new(Href: "/x", Title: "X", Description: "d", Icon: TagIcon),
        };

        var cut = ctx.Render(builder =>
        {
            builder.OpenComponent<DashboardSection>(0);
            builder.AddAttribute(1, nameof(DashboardSection.Title), "T");
            builder.AddAttribute(2, nameof(DashboardSection.Items), items);
            builder.AddAttribute(3, nameof(DashboardSection.ChildContent),
                (RenderFragment)(b => b.AddMarkupContent(0, "<p class=\"bespoke\">ignored</p>")));
            builder.CloseComponent();
        });

        cut.FindAll("a.card-link").Should().HaveCount(1);
        cut.FindAll(".bespoke").Should().BeEmpty();
    }

    [TestMethod]
    public void Items_RespectsPerItemMatchAndIconWidth()
    {
        using var ctx = new BunitContext();

        var items = new List<DashboardItem>
        {
            new(Href: "/exact", Title: "Exact", Description: "d", Icon: TagIcon,
                Match: NavLinkMatch.All, IconWidth: "64px"),
        };

        var cut = ctx.Render(builder =>
        {
            builder.OpenComponent<DashboardSection>(0);
            builder.AddAttribute(1, nameof(DashboardSection.Title), "T");
            builder.AddAttribute(2, nameof(DashboardSection.Items), items);
            builder.CloseComponent();
        });

        var card = cut.FindComponent<DashboardCard>();
        card.Instance.Match.Should().Be(NavLinkMatch.All);
        card.Instance.IconWidth.Should().Be("64px");
    }
}