using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit regression tests for the drawer Add-guardian mode switch
/// (<see cref="GuardianSection.DrawerAddMode"/>): once the user toggles the
/// title-row anchor to the existing-guardian selection surface, the surface
/// must survive re-renders and typeahead interaction. Regression: any parent
/// re-render re-ran <c>InitializeEditViewAsync</c>, whose IsAdd branch reset
/// <c>_drawerAddMode</c> back to NewGuardian, visibly snapping the drawer body
/// back to the blank new-guardian form as soon as the typeahead was focused
/// (click → OnDropDownExpandedAsync → OnOptionsSearch) or typed into.
/// </summary>
[TestClass]
public class GuardianDrawerAddModeTests : BunitContext
{
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public GuardianDrawerAddModeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        var http = new HttpClient(new EmptyJsonHandler())
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var codedValues = new SchoolCollab.Admin.Shared.Services.CodedValuesApiClient(http);
        var api = new SchoolCollab.Students.Application.Services.StudentsApiClient(
            http, NullLogger<SchoolCollab.Students.Application.Services.StudentsApiClient>.Instance, codedValues);

        Services.AddSingleton(codedValues);
        Services.AddSingleton(api);
        Services.AddSingleton<SchoolCollab.Students.Core.Contracts.IContactsClient>(api);
        Services.AddSingleton(NullLogger<GuardianSection>.Instance);
    }

    private sealed class EmptyJsonHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            });
    }

    private IRenderedComponent<GuardianSection> RenderAddDrawer()
        => Render<GuardianSection>(parameters => parameters
            .Add(p => p.View, GuardianSection.GuardianView.Edit)
            .Add(p => p.Mode, StudentFormFieldsMode.Inline)
            .Add(p => p.IsAdd, true)
            .Add(p => p.StudentId, StudentId)
            .Add(p => p.GuardianLinks, new List<GuardianAssignment>()));

    [TestMethod]
    public void AddDrawer_ToggleToExistingGuardian_SurvivesParentRerender()
    {
        // Arrange: drawer-add starts on the new-guardian surface.
        var cut = RenderAddDrawer();
        cut.Find(".guardian-edit-form").Should().NotBeNull("a fresh add starts on the new-guardian form");

        // Act: toggle to the existing-guardian selection surface.
        cut.Find(".guardian-drawer-add-toggle").Click();
        cut.WaitForState(() => cut.FindAll(".guardian-drawer-existing").Count > 0,
            timeout: TimeSpan.FromSeconds(5));
        cut.Find(".guardian-drawer-existing").Should().NotBeNull("the toggle switches to the existing-guardian surface");
        cut.FindAll(".guardian-edit-form").Should().BeEmpty("the new-guardian form is replaced, not shown alongside");

        // Act: simulate ANY ancestor re-render passing the same parameters
        // (OnParametersSet → InitializeEditViewAsync).
        cut.Render();

        // Assert: the user's chosen surface must not snap back.
        cut.Find(".guardian-drawer-existing").Should().NotBeNull(
            "a same-parameter re-render must not reset the drawer add mode to NewGuardian");
        cut.FindAll(".guardian-edit-form").Should().BeEmpty();
    }

    [TestMethod]
    public async Task AddDrawer_ToggleToExistingGuardian_SurvivesTypeaheadFocusAndInput()
    {
        // Arrange: toggle to the existing-guardian surface.
        var cut = RenderAddDrawer();
        cut.Find(".guardian-drawer-add-toggle").Click();
        cut.WaitForState(() => cut.FindAll(".guardian-drawer-existing").Count > 0,
            timeout: TimeSpan.FromSeconds(5));

        // Act: focus/click the typeahead input. In FluentAutocomplete v4 the
        // input's click handler runs OnDropDownExpandedAsync → InputHandlerAsync,
        // which debounces into InvokeOptionsSearchAsync (OnOptionsSearch callback).
        cut.Find("fluent-text-field").Click();

        // Act: type a search query (input path through the same pipeline).
        // Wait past the 300ms ImmediateDelay so the debounced
        // InvokeOptionsSearchAsync actually runs before asserting.
        cut.Find("fluent-text-field").Input("Al");
        await Task.Delay(500);

        // Assert: the existing-guardian surface is still showing.
        cut.Find(".guardian-drawer-existing").Should().NotBeNull(
            "typeahead focus/input must not revert the drawer body to the new-guardian form");
        cut.FindAll(".guardian-edit-form").Should().BeEmpty();
    }
}
