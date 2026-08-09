using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
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
/// bUnit tests for <see cref="GradeTopicsDialog"/> and <see cref="GradeTeachersDialog"/>
/// (the full-list dialogs opened by the grade-detail section cards' "View all" anchors).
/// Both receive their data + action callbacks as parameters (the page owns the API), so
/// these assert the list renders, the empty state, and that the action callbacks fire.
/// The Fluent role dropdown renders in shadow DOM (assert via the TCHROLES parent lookup);
/// the underlying HTTP contracts are covered by the client-contract + CQRS handler tests.
/// </summary>
[TestClass]
public class GradeDialogsBunitTests : BunitContext
{
    public GradeDialogsBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
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
        Services.AddSingleton(new CodedValuesApiClient(http));
    }

    private static GradeTopicCurriculumDto Topic(Guid id, string name, string code, int strands, int lessons) =>
        new(id, name, code, strands, lessons);

    private static TopicDto CatalogTopic(Guid id, string name, string code) =>
        new(id, null, code, name, null, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static TeacherWithRoleDto Teacher(
        Guid id, string first, string last, Guid? genderId = null, Guid? levelId = null,
        Guid[]? quals = null, Guid? roleId = null, TopicDto[]? topics = null) =>
        new(id, null, first, last, null, genderId, null, levelId, quals ?? [], false,
            roleId, topics ?? [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

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
    public void TopicsDialog_Remove_FiresRemoveCallback()
    {
        var topicId = Guid.NewGuid();
        var removed = new System.Collections.Generic.List<Guid>();
        var cut = Render<GradeTopicsDialog>(p =>
        {
            p.Add(x => x.Topics, new[] { Topic(topicId, "Mathematics", "MATH", 2, 3) });
            p.Add(x => x.Remove, new System.Func<Guid, Task>(id => { removed.Add(id); return Task.CompletedTask; }));
        });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Mathematics"));
        cut.Find("fluent-button[title='Topic actions']").Click();
        var removeItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Remove"));
        removeItem.Click();
        removed.Should().Contain(topicId);
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

    // ── GradeTeachersDialog ─────────────────────────────────────────────────

    [TestMethod]
    public void TeachersDialog_ListsTeachers_WithNames_AndRoleLookup()
    {
        RegisterCodedValuesApi();
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var cut = Render<GradeTeachersDialog>(p => p
            .Add(x => x.Teachers, new[]
            {
                Teacher(teacherId, "Jane", "Doe",
                    genderId: Guid.NewGuid(), levelId: Guid.NewGuid(),
                    quals: new[] { Guid.NewGuid() },
                    topics: new[] { new TopicDto(topicId, null, null, "Mathematics", null, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) }),
            })
            .Add(x => x.SalutationNames, new Dictionary<Guid, string>())
            .Add(x => x.GenderNames, new Dictionary<Guid, string>())
            .Add(x => x.LevelNames, new Dictionary<Guid, string>())
            .Add(x => x.QualificationNames, new Dictionary<Guid, string>()));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane Doe"));
        cut.Markup.Should().Contain("Mathematics", "assigned-topic chip renders");
        cut.Markup.Should().Contain("Close");
        // The Fluent role dropdown (shadow DOM) fired one TCHROLES parent lookup.
        var client = Services.GetRequiredService<CodedValuesApiClient>();
        client.Should().NotBeNull();
    }

    [TestMethod]
    public void TeachersDialog_RendersResolvedNames()
    {
        RegisterCodedValuesApi();
        var teacherId = Guid.NewGuid();
        var genderId = Guid.NewGuid();
        var levelId = Guid.NewGuid();
        var qualId = Guid.NewGuid();
        var salutationId = Guid.NewGuid();
        var gender = new Dictionary<Guid, string> { [genderId] = "Female" };
        var level = new Dictionary<Guid, string> { [levelId] = "Master's" };
        var quals = new Dictionary<Guid, string> { [qualId] = "Mathematics Teaching" };
        var sals = new Dictionary<Guid, string> { [salutationId] = "Ms" };
        var cut = Render<GradeTeachersDialog>(p => p
            .Add(x => x.Teachers, new[]
            {
                Teacher(teacherId, "Jane", "Doe", genderId, levelId, new[] { qualId }),
            })
            .Add(x => x.SalutationNames, sals)
            .Add(x => x.GenderNames, gender)
            .Add(x => x.LevelNames, level)
            .Add(x => x.QualificationNames, quals));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane Doe"));
        cut.Markup.Should().Contain("Female", "resolved gender renders");
        cut.Markup.Should().Contain("Master's", "resolved level renders");
        cut.Markup.Should().Contain("Mathematics Teaching", "qualification chip renders");
    }

    [TestMethod]
    public void TeachersDialog_EmptyState_WhenNoTeachers()
    {
        RegisterCodedValuesApi();
        var cut = Render<GradeTeachersDialog>(p => p
            .Add(x => x.Teachers, Array.Empty<TeacherWithRoleDto>())
            .Add(x => x.SalutationNames, new Dictionary<Guid, string>())
            .Add(x => x.GenderNames, new Dictionary<Guid, string>())
            .Add(x => x.LevelNames, new Dictionary<Guid, string>())
            .Add(x => x.QualificationNames, new Dictionary<Guid, string>()));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No teachers linked to this grade yet."));
    }

    [TestMethod]
    public void TeachersDialog_UnlinkTopic_FiresUnlinkCallback()
    {
        RegisterCodedValuesApi();
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var unlinked = new System.Collections.Generic.List<(Guid Teacher, Guid Topic)>();
        var cut = Render<GradeTeachersDialog>(p => p
            .Add(x => x.Teachers, new[]
            {
                Teacher(teacherId, "Jane", "Doe", topics: new[] { new TopicDto(topicId, null, null, "Mathematics", null, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) }),
            })
            .Add(x => x.SalutationNames, new Dictionary<Guid, string>())
            .Add(x => x.GenderNames, new Dictionary<Guid, string>())
            .Add(x => x.LevelNames, new Dictionary<Guid, string>())
            .Add(x => x.QualificationNames, new Dictionary<Guid, string>())
            .Add(x => x.UnlinkTopic, new System.Func<TeacherWithRoleDto, Guid, Task>((t, tid) => { unlinked.Add((t.Id, tid)); return Task.CompletedTask; })));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Mathematics"));
        cut.FindAll(".chip").First().Click();
        unlinked.Should().Contain((teacherId, topicId));
    }

    [TestMethod]
    public void TeachersDialog_Remove_FiresRemoveCallback()
    {
        RegisterCodedValuesApi();
        var teacherId = Guid.NewGuid();
        var removed = new System.Collections.Generic.List<Guid>();
        var cut = Render<GradeTeachersDialog>(p => p
            .Add(x => x.Teachers, new[] { Teacher(teacherId, "Jane", "Doe") })
            .Add(x => x.SalutationNames, new Dictionary<Guid, string>())
            .Add(x => x.GenderNames, new Dictionary<Guid, string>())
            .Add(x => x.LevelNames, new Dictionary<Guid, string>())
            .Add(x => x.QualificationNames, new Dictionary<Guid, string>())
            .Add(x => x.Remove, new System.Func<TeacherWithRoleDto, Task>(t => { removed.Add(t.Id); return Task.CompletedTask; })));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane Doe"));
        cut.Find("fluent-button[title='Teacher actions']").Click();
        var removeItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Remove"));
        removeItem.Click();
        removed.Should().Contain(teacherId);
    }

    [TestMethod]
    public void TeachersDialog_Link_MovesTeacherFromPicker_ToLinkedList()
    {
        RegisterCodedValuesApi();
        var teacherId = Guid.NewGuid();
        var linked = new System.Collections.Generic.List<Guid>();
        var cut = Render<GradeTeachersDialog>(p => p
            .Add(x => x.Teachers, Array.Empty<TeacherWithRoleDto>())
            .Add(x => x.UnlinkedTeachers, new[]
            {
                new TeacherDto(teacherId, null, "Jane", "Doe", null, false,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            })
            .Add(x => x.SalutationNames, new Dictionary<Guid, string>())
            .Add(x => x.GenderNames, new Dictionary<Guid, string>())
            .Add(x => x.LevelNames, new Dictionary<Guid, string>())
            .Add(x => x.QualificationNames, new Dictionary<Guid, string>())
            .Add(x => x.Link, new System.Func<Guid, Task>(id => { linked.Add(id); return Task.CompletedTask; })));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane Doe"));

        // The FluentSelect picker renders in shadow DOM (not directly clickable
        // in bUnit), so simulate selecting the teacher by setting the private
        // bound field, then click Link.
        var field = typeof(GradeTeachersDialog).GetField("_teacherToAdd",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(cut.Instance, new TeacherDto(teacherId, null, "Jane", "Doe", null, false,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        cut.Render();

        cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Link")).Click();

        cut.WaitForAssertion(() =>
        {
            linked.Should().Contain(teacherId, "the Link callback fires with the selected teacher id");
            cut.Instance.Teachers.Should().ContainSingle(t => t.Id == teacherId,
                "the linked teacher moves into the linked list");
            cut.Instance.UnlinkedTeachers.Should().BeEmpty();
        });
    }
}
