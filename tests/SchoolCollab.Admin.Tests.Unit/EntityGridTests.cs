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
