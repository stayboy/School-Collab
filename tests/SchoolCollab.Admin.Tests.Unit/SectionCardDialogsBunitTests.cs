using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the four dialogs introduced by the section-card adoption
/// (section-card-lessons-adoption.md): <see cref="TeacherRoleDialog"/>,
/// <see cref="TeacherSubjectsDialog"/>, <see cref="StudentCreateDialog"/>, and
/// <see cref="StudentEditDialog"/>. These are the net-new dialog components behind
/// the Teachers/Students card kebab actions; the page-level wiring is covered in
/// <c>GradeLevelDetailPageTests</c>. Here we assert each dialog's own load/render
/// and its key behaviour (Save callback, topic prefill, create/edit form).
/// </summary>
[TestClass]
public class SectionCardDialogsBunitTests : BunitContext
{
    public SectionCardDialogsBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();
        public ScriptedHandler Map(string url, HttpStatusCode status, string body)
        {
            _responses[("ANY", url)] = (status, body);
            return this;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            foreach (var kv in _responses)
            {
                if (kv.Key.Method != "ANY") continue;
                if (url.Equals(kv.Key.Url, System.StringComparison.OrdinalIgnoreCase))
                    return new HttpResponseMessage(kv.Value.Status) { Content = new StringContent(kv.Value.Body, Encoding.UTF8, "application/json") };
            }
            string? best = null; (HttpStatusCode Status, string Body) bestResp = default;
            foreach (var kv in _responses)
            {
                if (kv.Key.Method != "ANY") continue;
                if (url.StartsWith(kv.Key.Url, System.StringComparison.OrdinalIgnoreCase) && (best is null || kv.Key.Url.Length > best.Length))
                {
                    best = kv.Key.Url;
                    bestResp = kv.Value;
                }
            }
            if (best is not null)
                return new HttpResponseMessage(bestResp.Status) { Content = new StringContent(bestResp.Body, Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"Unexpected {url}", Encoding.UTF8, "application/json") };
        }
    }

    private void Register(ScriptedHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
    }

    private static string JsonArray(params object[] items) => JsonSerializer.Serialize(items);

    private static Dictionary<string, object?> TopicJson(Guid id, string name, string code) => new()
    {
        ["id"] = id, ["codedValueId"] = (Guid?)Guid.NewGuid(), ["code"] = code, ["name"] = name,
        ["description"] = (string?)null, ["displayOrder"] = 1,
        ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z",
    };

    private static Dictionary<string, object?> StudentJson(Guid id, string number, string first, string last) => new()
    {
        ["id"] = id, ["studentNumber"] = number, ["titleCodedValueId"] = (Guid?)null,
        ["firstName"] = first, ["lastName"] = last, ["dateOfBirth"] = (DateOnly?)null,
        ["genderCodedValueId"] = (Guid?)null, ["isDeleted"] = false,
        ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z",
    };

    // ── TeacherRoleDialog ────────────────────────────────────────────────────

    [TestMethod]
    public void TeacherRoleDialog_Save_InvokesCallback_WithCurrentRole()
    {
        // The dialog renders a CodedValueDropdown (needs CodedValuesApiClient).
        Register(new ScriptedHandler());
        var roleId = Guid.NewGuid();
        Guid? saved = null;
        var cut = Render<TeacherRoleDialog>(p => p
            .Add(x => x.CurrentRoleId, roleId)
            .Add(x => x.Save, new Func<Guid?, Task>(r => { saved = r; return Task.CompletedTask; })));

        cut.Markup.Should().Contain("Role", "the role dropdown label renders");
        cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Save")).Click();

        saved.Should().Be(roleId, "Save invokes the callback with the current role");
    }

    // ── TeacherSubjectsDialog ───────────────────────────────────────────────

    [TestMethod]
    public void TeacherSubjectsDialog_LoadsTopics_AndRendersAssigned()
    {
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("/students/topics", HttpStatusCode.OK, JsonArray(TopicJson(topicId, "Mathematics", "MATH")));
        handler.Map($"/teachers/{teacherId}/topics/roles", HttpStatusCode.OK, JsonArray(
            new Dictionary<string, object?>
            {
                ["topicId"] = topicId, ["roleCodedValueId"] = roleId,
                ["startDate"] = "2026-01-01", ["endDate"] = (string?)null,
            }));
        Register(handler);

        var cut = Render<TeacherSubjectsDialog>(p => p.Add(x => x.TeacherId, teacherId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Mathematics",
            "the dialog loads the topic catalog and renders each topic"));
        cut.Markup.Should().Contain("Start",
            "the assignment start date picker renders");
        cut.Markup.Should().Contain("End (open-ended)",
            "the assignment end date picker renders (open-ended allowed)");
        cut.Markup.Should().Contain("Save", "the dialog renders its Save action");
    }

    // ── StudentCreateDialog ─────────────────────────────────────────────────

    [TestMethod]
    public void StudentCreateDialog_RendersCreateForm()
    {
        Register(new ScriptedHandler());

        var cut = Render<StudentCreateDialog>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Create Student",
            "the create dialog renders the shared StudentFormFields with a Create action"));
    }

    // ── StudentEditDialog ───────────────────────────────────────────────────

    [TestMethod]
    public void StudentEditDialog_LoadsStudent_AndRendersEditForm()
    {
        var studentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/{studentId}", HttpStatusCode.OK, JsonSerializer.Serialize(StudentJson(studentId, "STU001", "Ada", "Lovelace")));
        handler.Map($"/students/{studentId}/guardians", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = Render<StudentEditDialog>(p => p.Add(x => x.StudentId, studentId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save Changes",
            "the edit dialog loads the student and renders the shared StudentFormFields in edit mode"));
    }
}
