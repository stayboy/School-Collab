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
        // GradeTopicsDialog injects a logger (FriendlyError.Describe) and the students client (its
        // enrolment switch reads the subject's audience size for the confirm). These fixtures never
        // set a GradeLevelId, so no call is made — the registrations only have to resolve.
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        RegisterCodedValuesApi();
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

    /// <summary>The bridge row id for the fixture's topic. The reorder endpoints
    /// target the ASSIGNMENT, not the topic (one topic can be assigned to several
    /// grades), so the DTO carries one; a fixed value keeps the rendered markup
    /// reproducible and never hands the move buttons an empty guid.</summary>
    private static readonly Guid BridgeAssignmentId = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b1");

    private static GradeTopicCurriculumDto Topic(
        Guid id, string name, string code, int strands, int lessons, bool requiresEnrollment = false) =>
        new(BridgeAssignmentId, id, name, code, strands, lessons, requiresEnrollment);

    private static TopicDto CatalogTopic(Guid id, string name, string code) =>
        new(id, null, code, name, null, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    /// <summary>Seeds the inputs <see cref="GradeTopicsDialog"/> takes as typed
    /// <c>[Parameter]</c>s (FluentUI 5.x spreads <c>DialogOptions.Parameters</c> onto the
    /// matching <c>[Parameter]</c>s by name; a bUnit render seeds them directly).</summary>
    private static void SeedTopics(
        ComponentParameterCollectionBuilder<GradeTopicsDialog> parameters,
        GradeTopicCurriculumDto[]? topics = null,
        TopicDto[]? unassigned = null,
        Func<Guid, Task>? remove = null,
        Func<Guid, Task<Guid>>? assign = null)
    {
        if (topics is not null) parameters.Add(p => p.Topics, topics);
        if (unassigned is not null) parameters.Add(p => p.UnassignedTopics, unassigned);
        if (remove is not null) parameters.Add(p => p.Remove, remove);
        if (assign is not null) parameters.Add(p => p.Assign, assign);
    }

    // ── GradeTopicsDialog ───────────────────────────────────────────────────

    [TestMethod]
    public void TopicsDialog_ListsAssignedTopics_WithCounts()
    {
        var topicId = Guid.NewGuid();
        var cut = Render<GradeTopicsDialog>(p => SeedTopics(p, topics: new[]
        {
            Topic(topicId, "Mathematics", "MATH", 2, 3),
        }));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Mathematics"));
        cut.Markup.Should().Contain("MATH", "topic code renders");
        // The View-all is a FluentDataGrid now: the strand count is the Strands
        // cell's own text (that cell is the link into the strands dialog), and the
        // lesson count keeps its "<n> lessons" tooltip.
        var strandsCell = cut.FindAll("fluent-link[title='Strands']").Should().ContainSingle().Subject;
        strandsCell.TextContent.Trim().Should().Be("2", "the strands column shows the topic's strand count");
        cut.Markup.Should().Contain("3 lessons", "the lessons column keeps its count tooltip");
        cut.Markup.Should().Contain("Close");
    }

    [TestMethod]
    public void TopicsDialog_EmptyState_WhenNoTopics()
    {
        var cut = Render<GradeTopicsDialog>(p => SeedTopics(p, topics: Array.Empty<GradeTopicCurriculumDto>()));
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
            .AddChildContent<GradeTopicsDialog>(child => SeedTopics(child,
                topics: new[] { Topic(topicId, "Mathematics", "MATH", 2, 3) },
                remove: new System.Func<Guid, Task>(id => { removed.Add(id); return Task.CompletedTask; }))));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Mathematics"));
        cut.Find("fluent-button[title='Topic actions']").Click();
        var removeItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Remove"));
        removeItem.MenuItemClick();

        // The destructive Remove action opens a MODAL confirmation dialog
        // (dark overlay) with a warning message and Primary/Secondary buttons.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("type=\"modal\""));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Remove subject 'Mathematics' from this grade"));
        cut.WaitForAssertion(() =>
            cut.FindAll(".confirm-dialog fluent-button[appearance='primary']").Any());
        cut.WaitForAssertion(() =>
            cut.FindAll(".confirm-dialog fluent-button[appearance='outline']").Should().NotBeEmpty(
                "the Cancel (secondary) button must render"));
    }

    [TestMethod]
    public void TopicsDialog_AssignButton_Revealed_WithUnassignedCatalog()
    {
        var topicId = Guid.NewGuid();
        var cut = Render<GradeTopicsDialog>(p =>
            SeedTopics(p,
                topics: Array.Empty<GradeTopicCurriculumDto>(),
                unassigned: new[] { CatalogTopic(topicId, "Science", "SCI") }));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Add a topic…"));
        cut.Markup.Should().Contain("Science", "unassigned catalog feeds the add-topic picker");
    }

    [TestMethod]
    public void TopicsDialog_Assign_MovesTopicFromPicker_ToAssignedList()
    {
        var topicId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var assigned = new System.Collections.Generic.List<Guid>();
        var cut = Render<GradeTopicsDialog>(p =>
            SeedTopics(p,
                topics: Array.Empty<GradeTopicCurriculumDto>(),
                unassigned: new[] { CatalogTopic(topicId, "Science", "SCI") },
                // The Assign callback returns the new bridge (assignment) id: the
                // dialog appends nothing for an empty id, so the stub must return a
                // real one for the row to reach the assigned list.
                assign: new System.Func<Guid, Task<Guid>>(id => { assigned.Add(id); return Task.FromResult(assignmentId); })));

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
            // The assigned topic moves into the assigned list (rendered in the dialog).
            cut.Markup.Should().Contain("Science", "the assigned topic moves into the assigned list");
        });
    }

}
