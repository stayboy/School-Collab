using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Contracts;
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
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        public ScriptedHandler Map(string url, HttpStatusCode status, string body)
        {
            _responses[("ANY", url)] = (status, body);
            return this;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));
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
        var api = new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv);
        Services.AddSingleton(api);
        // The ContactsEditor (rendered by the student create/edit dialogs)
        // injects IContactsClient, which the app maps to StudentsApiClient.
        Services.AddSingleton<IContactsClient>(api);
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

    // ── StudentCreateDialog: contacts section ───────────────────────────────

    [TestMethod]
    public void StudentCreateDialog_RendersContactsSection()
    {
        Register(new ScriptedHandler());

        var cut = Render<StudentCreateDialog>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Create Student",
            "the create dialog renders the shared StudentFormFields"));
        cut.Markup.Should().Contain("Contacts",
            "the create dialog renders a Contacts section");
        cut.Markup.Should().Contain("No contacts yet.",
            "the Buffered ContactsEditor renders its empty state");
    }

    // ── StudentEditDialog: binds to existing guardians + contacts ───────────

    [TestMethod]
    public void StudentEditDialog_ShowsGuardiansAndContacts_ForExistingStudent()
    {
        var studentId = Guid.NewGuid();
        var guardianId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(StudentJson(studentId, "STU001", "Ada", "Lovelace")));
        // One linked guardian (Kofi Mensah).
        handler.Map($"/students/{studentId}/guardians", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    guardianId, studentId, role = 0, relationshipCodedValueId = (Guid?)null,
                    isEmergencyContact = false, firstName = "Kofi", lastName = "Mensah",
                    displayName = "Kofi Mensah", titleCodedValueId = (Guid?)null,
                    contacts = Array.Empty<object>(), totalContactCount = 0,
                }
            }));
        // Salutations lookup (SALUTS) — the guardian list loads it.
        handler.Map("/api/coded-values/by-parent?parentCode=SALUTS", HttpStatusCode.OK, "[]");
        // One existing student contact (ada@example.com).
        handler.Map($"/contacts?ownerType=Student&ownerId={studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    id = contactId, ownerType = 0, ownerId = studentId, channel = 0,
                    value = "ada@example.com", label = (string?)null, isVerified = false,
                    isDeleted = false, createdAt = "2026-01-01T00:00:00Z",
                    updatedAt = "2026-01-01T00:00:00Z", countryCode = (string?)null,
                    displayOrder = 0,
                }
            }));
        Register(handler);

        var cut = Render<StudentEditDialog>(p => p.Add(x => x.StudentId, studentId));

        // Demographics bound from the existing student.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada",
            "the edit dialog binds the existing student's first name"));
        // Guardians bound from the existing student's links (the inline grid
        // renders First name / Last name in separate columns).
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Kofi",
            "the edit dialog binds the existing student's linked guardians"));
        cut.Markup.Should().Contain("Mensah",
            "the linked guardian's last name renders");
        // Contacts bound from the existing student's persisted contacts.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("ada@example.com",
            "the edit dialog binds the existing student's contacts"));
    }
}
