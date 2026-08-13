using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
// Aliases resolve the namespace collisions between the Services (API-serialization)
// records and the Core DTOs, matching how Detail.razor references them.
using GradeTopicCurriculumDto = SchoolCollab.Students.Application.Services.GradeTopicCurriculumDto;
using TeacherDto = SchoolCollab.Students.Application.Services.TeacherDto;
using TeacherWithRoleDto = SchoolCollab.Students.Core.DTOs.TeacherWithRoleDto;
using TopicDto = SchoolCollab.Students.Core.DTOs.TopicDto;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="GradeTopicsDialog"/>
/// (the full-list dialog opened by the grade-detail Subjects card's "View all" anchor).
/// The dialog receives its data + action callbacks as parameters (the page owns the API), so
/// these assert the list renders, the empty state, and that the action callbacks fire.
/// </summary>
[TestClass]
public class GradeDialogsBunitTests : BunitContext
{
    public GradeDialogsBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>
    /// Hosts a <see cref="FluentDialogProvider"/> alongside a child component so
    /// destructive row actions (which show a confirmation prompt via
    /// <c>IDialogService</c>) can render their dialog in the provider.
    /// </summary>
    private sealed class DialogHost : ComponentBase
    {
        [Parameter] public RenderFragment? ChildContent { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<FluentDialogProvider>(0);
            builder.CloseComponent();
            builder.AddContent(1, ChildContent);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url)> Calls = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery));
            // TCHROLES parent lookup returns an empty list.
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        }
    }

    private void RegisterCodedValuesApi()
    {
        var handler = new RecordingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
    }

    private static GradeTopicCurriculumDto Topic(Guid id, string name, string code, int strands, int lessons) =>
        new(id, name, code, strands, lessons);

    private static TopicDto CatalogTopic(Guid id, string name, string code) =>
        new(id, null, code, name, null, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    // ── GradeTopicsDialog ───────────────────────────────────────────────────

    [TestMethod]
    public void TopicsDialog_ListsAssignedTopics_WithCounts()
    {
        var topicId = Guid.NewGuid();
        var cut = Render<GradeTopicsDialog>(p => p.Add(x => x.Topics, new[]
        {
            Topic(topicId, "Mathematics", "MATH", 2, 3),
        }));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Mathematics"));
        cut.Markup.Should().Contain("MATH", "topic code renders");
        cut.Markup.Should().Contain("2 strands");
        cut.Markup.Should().Contain("3 lessons");
        cut.Markup.Should().Contain("Close");
    }

    [TestMethod]
    public void TopicsDialog_EmptyState_WhenNoTopics()
    {
        var cut = Render<GradeTopicsDialog>(p => p.Add(x => x.Topics, Array.Empty<GradeTopicCurriculumDto>()));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No topics assigned to this grade yet."));
    }

    [TestMethod]
    public void TopicsDialog_Remove_OpensModalConfirmation()
    {
        var topicId = Guid.NewGuid();
        var removed = new System.Collections.Generic.List<Guid>();
        // The destructive Remove action is gated by a confirmation prompt that
        // renders in a FluentDialogProvider, so host the dialog under one.
        var cut = Render<DialogHost>(p => p
            .AddChildContent<GradeTopicsDialog>(child => child
                .Add(x => x.Topics, new[] { Topic(topicId, "Mathematics", "MATH", 2, 3) })
                .Add(x => x.Remove, new System.Func<Guid, Task>(id => { removed.Add(id); return Task.CompletedTask; }))));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Mathematics"));
        cut.Find("fluent-button[title='Topic actions']").Click();
        var removeItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Remove"));
        removeItem.Click();

        // The destructive Remove action opens a MODAL confirmation dialog
        // (dark overlay) with a warning message and Primary/Secondary buttons.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("modal=\"true\""));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Remove subject 'Mathematics' from this grade"));
        cut.WaitForAssertion(() =>
            cut.FindAll(".confirm-dialog fluent-button[appearance='accent']").Any());
        cut.WaitForAssertion(() =>
            cut.FindAll(".confirm-dialog fluent-button[appearance='outline']").Should().NotBeEmpty(
                "the Cancel (secondary) button must render"));
    }

    [TestMethod]
    public void TopicsDialog_AssignButton_Revealed_WithUnassignedCatalog()
    {
        var topicId = Guid.NewGuid();
        var cut = Render<GradeTopicsDialog>(p => p
            .Add(x => x.Topics, Array.Empty<GradeTopicCurriculumDto>())
            .Add(x => x.UnassignedTopics, new[] { CatalogTopic(topicId, "Science", "SCI") }));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Add a topic…"));
        cut.Markup.Should().Contain("Science", "unassigned catalog feeds the add-topic picker");
    }

    [TestMethod]
    public void TopicsDialog_Assign_MovesTopicFromPicker_ToAssignedList()
    {
        var topicId = Guid.NewGuid();
        var assigned = new System.Collections.Generic.List<Guid>();
        var cut = Render<GradeTopicsDialog>(p => p
            .Add(x => x.Topics, Array.Empty<GradeTopicCurriculumDto>())
            .Add(x => x.UnassignedTopics, new[] { CatalogTopic(topicId, "Science", "SCI") })
            .Add(x => x.Assign, new System.Func<Guid, Task>(id => { assigned.Add(id); return Task.CompletedTask; })));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Science"));

        // The FluentSelect picker renders in shadow DOM (not directly clickable
        // in bUnit), so simulate selecting the topic by setting the private
        // bound field, then click Assign.
        var field = typeof(GradeTopicsDialog).GetField("_topicToAdd",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(cut.Instance, CatalogTopic(topicId, "Science", "SCI"));
        cut.Render();

        cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Assign")).Click();

        cut.WaitForAssertion(() =>
        {
            assigned.Should().Contain(topicId, "the Assign callback fires with the selected topic id");
            cut.Instance.Topics.Should().ContainSingle(t => t.TopicId == topicId,
                "the assigned topic moves into the assigned list");
            cut.Instance.UnassignedTopics.Should().BeEmpty();
        });
    }

}
