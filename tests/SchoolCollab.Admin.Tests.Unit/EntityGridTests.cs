using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Landing;
using SchoolCollab.Admin.Shared.Components;
using System.Collections.Generic;
using System.Linq;

namespace SchoolCollab.Admin.Tests.Unit;

[TestClass]
public class EntityGridTests : BunitContext
{
    public class TestItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public EntityGridTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    [TestMethod]
    public void Paginator_Renders_When_ShowPager_IsTrue()
    {
        // Arrange
        var items = Enumerable.Range(1, 20).Select(i => new TestItem { Id = i, Name = $"Item {i}" }).ToList();
        var settings = new LandingGridSettings 
        { 
            GridTemplateColumns = "1fr 1fr", 
            ItemsPerPage = 10 
        };

        // Act
        var cut = Render<EntityGrid<TestItem>>(parameters => parameters
            .Add(p => p.Items, items.ToArray())
            .Add(p => p.GridSettings, settings)
            .Add(p => p.ShowPager, true)
        );

        // Assert
        var paginator = cut.FindComponent<FluentPaginator>();
        paginator.Should().NotBeNull();
    }

    [TestMethod]
    public void Paginator_Hidden_When_Items_Fit_One_Page()
    {
        // Arrange — fewer rows than a page: a pager would be two dead arrows (Q162).
        var items = Enumerable.Range(1, 3).Select(i => new TestItem { Id = i, Name = $"Item {i}" }).ToList();
        var settings = new LandingGridSettings { GridTemplateColumns = "1fr 1fr" };

        // Act — ShowPager is not set at all: the default is what is under test.
        var cut = Render<EntityGrid<TestItem>>(parameters => parameters
            .Add(p => p.Items, items.ToArray())
            .Add(p => p.GridSettings, settings)
        );

        // Assert
        cut.FindComponents<FluentPaginator>().Should().BeEmpty(
            "a grid whose rows fit one page renders no pager, and the default must not force one");
    }

    [TestMethod]
    public void Paginator_Renders_By_Default_When_Items_Exceed_One_Page()
    {
        // Arrange — default page size is 15, so 20 rows page.
        var items = Enumerable.Range(1, 20).Select(i => new TestItem { Id = i, Name = $"Item {i}" }).ToList();
        var settings = new LandingGridSettings { GridTemplateColumns = "1fr 1fr" };

        // Act — ShowPager is not set: paging is the default, per the rules file.
        var cut = Render<EntityGrid<TestItem>>(parameters => parameters
            .Add(p => p.Items, items.ToArray())
            .Add(p => p.GridSettings, settings)
        );

        // Assert
        cut.FindComponents<FluentPaginator>().Should().ContainSingle();
    }

    [TestMethod]
    public void Paginator_Default_Page_Size_Is_Fifteen()
    {
        new LandingGridSettings().ItemsPerPage.Should().Be(15,
            "the repo default is 15 (blazor-components.md § Grids are paged by default)");
    }

    [TestMethod]
    public void PaginationState_Initializes_With_Settings_Value()
    {
        // Arrange
        var items = Enumerable.Range(1, 20).Select(i => new TestItem { Id = i, Name = $"Item {i}" }).ToList();
        var settings = new LandingGridSettings 
        { 
            GridTemplateColumns = "1fr 1fr", 
            ItemsPerPage = 5 
        };

        // Act
        var cut = Render<EntityGrid<TestItem>>(parameters => parameters
            .Add(p => p.Items, items.ToArray())
            .Add(p => p.GridSettings, settings)
            .Add(p => p.ShowPager, true)
        );

        // Assert
        var paginator = cut.FindComponent<FluentPaginator>();
        var state = paginator.Instance.State;
        state.ItemsPerPage.Should().Be(5);
    }
}
