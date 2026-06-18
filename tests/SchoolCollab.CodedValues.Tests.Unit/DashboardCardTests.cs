using Bunit;
using FluentAssertions;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Components;
using PeopleIcon = Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size24.People;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

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
            builder.OpenComponent<DashboardCard<PeopleIcon>>(0);
            builder.AddAttribute(1, nameof(DashboardCard<PeopleIcon>.Href), "/students");
            builder.AddAttribute(2, nameof(DashboardCard<PeopleIcon>.Title), "Students");
            builder.AddAttribute(3, nameof(DashboardCard<PeopleIcon>.Description), "Manage student records.");
            builder.CloseComponent();
        });

        var card = cut.FindComponents<DashboardCard<PeopleIcon>>().Single();

        card.Markup.Should().Contain("Students");
        cut.Markup.Should().Contain("Manage student records.");
        cut.Markup.Should().Contain("Href=\"/students\"");
    }
}
