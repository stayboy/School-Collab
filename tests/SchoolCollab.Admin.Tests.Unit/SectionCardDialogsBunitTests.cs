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

    /// <summary>
    /// Renders StudentEditDialog as FluentUI would: its data comes via the
    /// <c>Content</c> DialogParameters indexer (StudentId key), because FluentUI
    /// does NOT spread indexer entries onto separate <c>[Parameter]</c>s.
    /// </summary>
    private IRenderedComponent<StudentEditDialog> RenderEditDialog(Guid studentId)
    {
        var content = new DialogParameters { [StudentEditDialog.StudentIdKey] = studentId };
        return Render<StudentEditDialog>(p => p.Add(x => x.Content, content));
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
        // FluentUI passes dialog inputs via the Content DialogParameters indexer.
        var content = new DialogParameters
        {
            [TeacherRoleDialog.CurrentRoleIdKey] = roleId,
            [TeacherRoleDialog.SaveKey] = new Func<Guid?, Task>(r => { saved = r; return Task.CompletedTask; }),
        };
        var cut = Render<TeacherRoleDialog>(p => p.Add(x => x.Content, content));

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

        var cut = RenderEditDialog(studentId);

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
    public void StudentEditDialog_BindsStudentProfile_ToInputFields()
    {
        // Regression: the edit dialog must bind the existing student's profile
        // into the form input values (not leave them blank). The form is gated
        // on the profile load so the FluentTextField bindings connect with the
        // populated model — without that, fluent-text-field's Web Component does
        // not re-sync its internal value after an async model update and the
        // inputs render empty.
        var studentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(StudentJson(studentId, "STU001", "Ada", "Lovelace")));
        handler.Map($"/students/{studentId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map("/api/coded-values/by-parent?parentCode=SALUTS", HttpStatusCode.OK, "[]");
        handler.Map($"/contacts?ownerType=Student&ownerId={studentId}", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = RenderEditDialog(studentId);

        cut.WaitForAssertion(() => cut.Find("#studentFormFirstName").GetAttribute("value")
            .Should().Be("Ada", "First name binds the existing student profile"));
        cut.Find("fluent-text-field[placeholder='Last name']").GetAttribute("value")
            .Should().Be("Lovelace", "Last name binds the existing student profile");
        cut.Find("#studentFormNumber").GetAttribute("value")
            .Should().Be("STU001", "Student number binds the existing student profile");
    }

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

        var cut = RenderEditDialog(studentId);

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

    // ── StudentEditDialog: all-inclusive atomic save ─────────────────────────

    /// <summary>A student JSON with the required form fields populated (DOB + gender)
    /// so the shared form's DataAnnotations validation passes and Save fires.</summary>
    private static Dictionary<string, object?> ValidStudentJson(Guid id, string number, string first, string last) => new()
    {
        ["id"] = id, ["studentNumber"] = number, ["titleCodedValueId"] = (Guid?)null,
        ["firstName"] = first, ["lastName"] = last, ["dateOfBirth"] = "2015-03-10",
        ["genderCodedValueId"] = Guid.NewGuid(), ["isDeleted"] = false,
        ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z",
    };

    [TestMethod]
    public void StudentEditDialog_Save_IssuesOneAtomicUpdateRequest()
    {
        // The all-inclusive edit dialog must save profile + guardians + contacts in ONE
        // request to PUT /students/{id}/with-linked-data (not the old profile-only
        // UpdateStudentAsync, and not per-row link/unlink/contact calls).
        var studentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(ValidStudentJson(studentId, "STU001", "Ada", "Lovelace")));
        handler.Map($"/students/{studentId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map($"/contacts?ownerType=Student&ownerId={studentId}", HttpStatusCode.OK, "[]");
        handler.Map($"/students/{studentId}/with-linked-data", HttpStatusCode.NoContent, "");
        // The gender/title coded-value dropdowns resolve the selected ids.
        handler.Map("/api/coded-values/by-parent?parentCode=GENDER", HttpStatusCode.OK, "[]");
        handler.Map("/api/coded-values/by-parent?parentCode=SALUTS", HttpStatusCode.OK, "[]");
        // EnrichSingleAsync resolves the gender name for a non-null gender id.
        handler.Map("/api/coded-values/by-ids", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = RenderEditDialog(studentId);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save Changes"));
        // Confirm the model loaded with the required fields (so the form can validate).
        cut.WaitForAssertion(() => cut.Find("#studentFormFirstName").GetAttribute("value")
            .Should().Be("Ada", "the model must load before Save can validate"));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => handler.Calls.Count(c => c.Method == "PUT").Should().Be(1,
            "Save must issue exactly one atomic update request"));
        var put = handler.Calls.Single(c => c.Method == "PUT");
        put.Url.Should().Be($"/students/{studentId}/with-linked-data");
        var req = JsonSerializer.Deserialize<UpdateStudentWithLinkedDataRequest>(put.Body!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        req.FirstName.Should().Be("Ada");
        req.LastName.Should().Be("Lovelace");
        req.ExpectedRowVersion.Should().Be(0);
        req.LoadedGuardianIds.Should().BeEmpty();
        req.LoadedContactIds.Should().BeEmpty();
    }

    [TestMethod]
    public void StudentEditDialog_Cancel_IssuesNoRequests()
    {
        var studentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(ValidStudentJson(studentId, "STU001", "Ada", "Lovelace")));
        handler.Map($"/students/{studentId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map($"/contacts?ownerType=Student&ownerId={studentId}", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = RenderEditDialog(studentId);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save Changes"));

        cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Cancel")).Click();

        handler.Calls.Should().NotContain(c => c.Method == "PUT",
            "Cancel must not issue an update request");
    }

    [TestMethod]
    public void StudentEditDialog_ConcurrencyConflict_ShowsReload()
    {
        // A 409 (stale ExpectedRowVersion or a concurrent guardian/contact change) must
        // surface a "changed by someone else — reload and retry" message with a Reload
        // action, not a hard failure or a silent close.
        var studentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(ValidStudentJson(studentId, "STU001", "Ada", "Lovelace")));
        handler.Map($"/students/{studentId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map($"/contacts?ownerType=Student&ownerId={studentId}", HttpStatusCode.OK, "[]");
        handler.Map($"/students/{studentId}/with-linked-data", HttpStatusCode.Conflict,
            "{\"message\":\"The entity was modified by another user.\"}");
        // The gender/title coded-value dropdowns resolve the selected ids.
        handler.Map("/api/coded-values/by-parent?parentCode=GENDER", HttpStatusCode.OK, "[]");
        handler.Map("/api/coded-values/by-parent?parentCode=SALUTS", HttpStatusCode.OK, "[]");
        // EnrichSingleAsync resolves the gender name for a non-null gender id.
        handler.Map("/api/coded-values/by-ids", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = RenderEditDialog(studentId);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save Changes"));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("changed by someone else",
            "a 409 must surface the concurrency message"));
        cut.Markup.Should().Contain("Reload",
            "a 409 must offer a Reload action");
    }

    [TestMethod]
    public void StudentEditDialog_ShowsLoadedGuardianRelationship()
    {
        // Regression: the all-inclusive edit dialog loads guardians into
        // Model.GuardianLinks (not the old live _links), so StudentFormFields must
        // resolve the relationship display names for those pre-loaded guardians —
        // otherwise the Inline grid's Relationship column renders blank.
        var studentId = Guid.NewGuid();
        var relId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(ValidStudentJson(studentId, "STU001", "Ada", "Lovelace")));
        handler.Map($"/students/{studentId}/guardians", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    guardianId = Guid.NewGuid(), studentId, role = 0,
                    relationshipCodedValueId = (Guid?)relId, isEmergencyContact = false,
                    firstName = "Kofi", lastName = "Mensah", displayName = "Kofi Mensah",
                    titleCodedValueId = (Guid?)null, contacts = Array.Empty<object>(),
                    totalContactCount = 0,
                }
            }));
        handler.Map($"/contacts?ownerType=Student&ownerId={studentId}", HttpStatusCode.OK, "[]");
        handler.Map("/api/coded-values/by-parent?parentCode=GENDER", HttpStatusCode.OK, "[]");
        handler.Map("/api/coded-values/by-parent?parentCode=SALUTS", HttpStatusCode.OK, "[]");
        handler.Map("/api/coded-values/by-ids", HttpStatusCode.OK, "[]");
        // The relationship name resolver (StudentFormFields.OnInitializedAsync -> EnsureRelNameAsync -> GetByIdAsync).
        handler.Map($"/api/coded-values/{relId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(new
            {
                id = relId, code = "MOTHER", name = "Mother", description = (string?)null,
                parentId = (Guid?)null, parentCode = (string?)null, isDisabled = false,
                displayOrder = 0, createdAt = "2026-01-01T00:00:00Z", updatedAt = "2026-01-01T00:00:00Z",
                attributes = Array.Empty<object>(), attributeDefinitions = Array.Empty<object>(),
            }));
        Register(handler);

        var cut = RenderEditDialog(studentId);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Mother",
            "the loaded guardian's relationship name must display (resolved from the coded value)"));
    }
}
